﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Magpie.Compilation
{
    /// <summary>
    /// Expression visitor that compiles expressions down to bytecode.
    /// </summary>
    public class BytecodeGenerator : IBoundExprVisitor<bool>
    {
        public int Position { get { return (int)mWriter.BaseStream.Position; } }

        private BytecodeGenerator(Compiler compiler, BinaryWriter writer,
            OffsetTable functionPatcher, StringTable stringTable)
        {
            mCompiler = compiler;
            mWriter = writer;
            mFunctionPatcher = functionPatcher;
            mStrings = stringTable;
            mJumpTable = new JumpTable(this);
        }

        public static void Generate(Compiler compiler, BinaryWriter writer,
            OffsetTable functionPatcher, StringTable stringTable, BoundFunction function)
        {
            BytecodeGenerator generator = new BytecodeGenerator(compiler,
                writer, functionPatcher, stringTable);

            functionPatcher.DefineOffset(function.Name);

            writer.Write(function.NumLocals);

            function.Body.Accept(generator);

            generator.Write(OpCode.Return);
        }

        public void SeekTo(int position)
        {
            mWriter.Seek(position, SeekOrigin.Begin);
        }

        public void SeekToEnd()
        {
            mWriter.Seek(0, SeekOrigin.End);
        }

        #region IBoundExprVisitor Members

        bool IBoundExprVisitor<bool>.Visit(UnitExpr expr) { return true; } // do nothing
        bool IBoundExprVisitor<bool>.Visit(BoolExpr expr) { Write(OpCode.PushBool, expr.Value ? (byte)1 : (byte)0); return true; }
        bool IBoundExprVisitor<bool>.Visit(IntExpr expr) { Write(OpCode.PushInt, expr.Value); return true; }

        bool IBoundExprVisitor<bool>.Visit(StringExpr expr)
        {
            Write(OpCode.PushString);
            mStrings.InsertOffset(expr.Value);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(BoundFuncRefExpr expr)
        {
            Write(OpCode.PushInt);
            mFunctionPatcher.InsertOffset(expr.Function.Name);
            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(ForeignCallExpr expr)
        {
            // evaluate the arg
            expr.Arg.Accept(this);

            // add the foreign call
            OpCode op;
            switch (expr.Function.FuncType.Parameters.Count)
            {
                case 0: op = OpCode.ForeignCall0; break;
                case 1: op = OpCode.ForeignCall1; break;
                default: op = OpCode.ForeignCallN; break;
            }

            Write(op, expr.Function.ID);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(BoundTupleExpr tuple)
        {
            // must visit in forward order to ensure that function arguments are
            // evaluated left to right
            for (int i = 0; i < tuple.Fields.Count; i++)
            {
                tuple.Fields[i].Accept(this);
            }

            // create the structure
            Write(OpCode.Alloc, tuple.Fields.Count);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(IntrinsicExpr expr)
        {
            expr.Arg.Accept(this);

            expr.Intrinsic.OpCodes.ForEach(op => Write(op));

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(BoundCallExpr expr)
        {
            expr.Arg.Accept(this);
            expr.Target.Accept(this);

            // add the call
            OpCode op;
            switch (expr.Arg.Type.Expanded.Length)
            {
                case 0: op = OpCode.Call0; break;
                case 1: op = OpCode.Call1; break;
                default: op = OpCode.CallN; break;
            }
            Write(op);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(BoundArrayExpr expr)
        {
            // the count is the first element
            Write(OpCode.PushInt, expr.Elements.Count);

            // must visit in forward order to ensure that array elements are
            // evaluated left to right
            for (int i = 0; i < expr.Elements.Count; i++)
            {
                expr.Elements[i].Accept(this);
            }

            // create the structure (+1 for the size)
            Write(OpCode.Alloc, expr.Elements.Count + 1);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(BoundBlockExpr block)
        {
            block.Exprs.ForEach(expr => expr.Accept(this));

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(BoundIfDoExpr expr)
        {
            // evaluate the condition
            expr.Condition.Accept(this);
            mJumpTable.JumpIfFalse("end");

            // execute the body
            expr.Body.Accept(this);

            // jump past it
            mJumpTable.PatchJump("end");

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(BoundIfThenExpr expr)
        {
            // evaluate the condition
            expr.Condition.Accept(this);
            mJumpTable.JumpIfFalse("else");

            // thenBody
            expr.ThenBody.Accept(this);

            // jump to end
            mJumpTable.Jump("end");

            // elseBody
            mJumpTable.PatchJump("else");
            expr.ElseBody.Accept(this);

            // end
            mJumpTable.PatchJump("end");

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(BoundWhileExpr expr)
        {
            mJumpTable.PatchJumpBack("while");

            // evaluate the condition
            expr.Condition.Accept(this);
            mJumpTable.JumpIfFalse("end");

            // body
            expr.Body.Accept(this);

            // jump back to loop
            mJumpTable.JumpBack("while");

            // exit loop
            mJumpTable.PatchJump("end");

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(LoadExpr expr)
        {
            expr.Struct.Accept(this);

            Write(OpCode.Load, (byte)expr.Index);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(StoreExpr expr)
        {
            expr.Value.Accept(this);
            expr.Struct.Accept(this);

            Write(OpCode.Store, expr.Field.Index);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(LocalsExpr expr)
        {
            //### bob: will need to handle other scopes at some point
            Write(OpCode.PushLocals);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(LoadElementExpr expr)
        {
            expr.Index.Accept(this);
            expr.Array.Accept(this);

            Write(OpCode.LoadArray);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(StoreElementExpr expr)
        {
            expr.Value.Accept(this);
            expr.Index.Accept(this);
            expr.Array.Accept(this);

            Write(OpCode.StoreArray);

            return true;
        }

        bool IBoundExprVisitor<bool>.Visit(ConstructExpr expr)
        {
            // just push the arg tuple onto the stack
            Write(OpCode.PushLocals);
            Write(OpCode.Load, (byte)0);

            // if the struct has only one field, the arg tuple will just be a
            // value. in that case, we need to hoist it into a struct to make
            // it properly a reference type.
            //### opt: this is really only needed for mutable single-field
            //    structs. for immutable ones, pass by reference and pass by value
            //    are indistinguishable.
            if (expr.Struct.Fields.Count == 1)
            {
                Write(OpCode.Alloc, 1);
            }

            return true;

        }

        bool IBoundExprVisitor<bool>.Visit(ConstructUnionExpr expr)
        {
            // load the case tag
            Write(OpCode.PushInt, expr.Case.Index);

            // one slot for the case tag
            int numSlots = 1;

            // load the value (if any)
            if (expr.Case.ValueType != Decl.Unit)
            {
                Write(OpCode.PushLocals);
                Write(OpCode.Load, (byte)0);

                // add a slot for the value
                numSlots++;
            }

            // create the structure
            Write(OpCode.Alloc, numSlots);

            return true;
        }

        #endregion

        public void Write(OpCode op)
        {
            mWriter.Write((byte)op);
        }

        public void Write(byte value)
        {
            mWriter.Write(value);
        }

        public void Write(int value)
        {
            mWriter.Write(value);
        }

        public void Write(OpCode op, byte operand)
        {
            Write(op);
            Write(operand);
        }

        public void Write(OpCode op, int operand)
        {
            Write(op);
            Write(operand);
        }

        private Compiler mCompiler;
        private BinaryWriter mWriter;
        private OffsetTable mFunctionPatcher;
        private StringTable mStrings;
        private JumpTable mJumpTable;
    }
}