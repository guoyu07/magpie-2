#!/usr/bin/python

# Runs the language tests.
from os import listdir
from os.path import abspath, dirname, isdir, join, realpath, relpath, splitext
import re
from subprocess import Popen, PIPE
import sys

MAGPIE_DIR = dirname(dirname(realpath(__file__)))
TEST_DIR = join(MAGPIE_DIR, 'test')
# TODO(bob): Support other platforms and configurations.
MAGPIE_APP = join(MAGPIE_DIR, 'build', 'Debug', 'magpie')

EXPECT_PATTERN = re.compile(r'// expect: (.*)')
EXPECT_ERROR_PATTERN = re.compile(r'// expect error')
ERROR_PATTERN = re.compile(r'line (\d+) col \d+\] Error: ')

class color:
    GREEN = '\033[32m'
    RED = '\033[31m'
    DEFAULT = '\033[0m'
    PINK = '\033[91m'

def walk(dir, callback):
    """ Walks [dir], and executes [callback] on each file. """
    dir = abspath(dir)
    for file in [file for file in listdir(dir) if not file in [".",".."]]:
        nfile = join(dir, file)
        if isdir(nfile):
            walk(nfile, callback)
        else:
            callback(nfile)


def run_test(path):
    if (splitext(path)[1] != '.mag'):
        return

    # Check if we are just running a subset of the tests.
    if len(sys.argv) == 2:
        this_test = relpath(path, join(MAGPIE_DIR, 'test'))
        if not this_test.startswith(sys.argv[1]):
            return

    # Make a nice short path relative to the working directory.
    path = relpath(path)

    # Read the test and parse out the expectations.
    expect_output = []
    expect_error = []
    i = 1
    with open(path, 'r') as file:
        for line in file:
            match = EXPECT_PATTERN.search(line)
            if match:
                expect_output.append((match.group(1), i))
            match = EXPECT_ERROR_PATTERN.search(line)
            if match:
                expect_error.append(i)
            i += 1

    # Invoke magpie and run the test.
    proc = Popen([MAGPIE_APP, path], stdout=PIPE, stderr=PIPE)
    (out, err) = proc.communicate()

    fails = []

    # Validate that no unexpected errors occurred.
    if err != '':
        for line in err.split('\n'):
            match = ERROR_PATTERN.search(line)
            if match:
                if not float(match.group(1)) in expect_error:
                    fails.append('Unexpected error:')
                    fails.append(line)
            elif line != '':
                fails.append('Unexpected output on stderr:')
                fails.append(line)
    else:
        for line in expect_error:
            fails.append('Expected error on line ' + str(line) + ' and got none.')

    # Validate the return code.
    # TODO(bob): Allow tests to specify expected return code.
    expect_return = 0

    # If we expect compile errors in the test, it should return 1.
    if len(expect_error) > 0:
        expect_return = 1

    if proc.returncode != expect_return:
        fails.append('Expected return code {0} and got {1}.'.
            format(expect_return, proc.returncode))

    # Validate the output.
    expect_index = 0

    # Remove the trailing last empty line.
    out_lines = out.split('\n')
    if out_lines[-1] == '':
        del out_lines[-1]

    for line in out_lines:
        if expect_index >= len(expect_output):
            fails.append('Got output "{0}" when none was expected.'.
                format(line))
        elif expect_output[expect_index][0] != line:
            fails.append('Expected output "{0}" on line {1} and got "{2}".'.
                format(expect_output[expect_index][0],
                       expect_output[expect_index][1], line))
        expect_index += 1

    while expect_index < len(expect_output):
        fails.append('Missing expected output "{0}" on line {1}.'.
            format(expect_output[expect_index][0],
                   expect_output[expect_index][1]))
        expect_index += 1

    # Display the results.
    if len(fails) == 0:
        print color.GREEN + 'PASS' + color.DEFAULT + ': ' + path
    else:
        print color.RED + 'FAIL' + color.DEFAULT + ': ' + path
        for fail in fails:
            print '     ', color.PINK + fail + color.DEFAULT
        print

walk(TEST_DIR, run_test)