#pragma once

#include "Array.h"
#include "Memory.h"
#include "Ast.h"

namespace magpie
{
  class Scope2;
  
  class Resolver : public ExprVisitor
  {
    friend class Scope2;
    
  public:
    static void resolve(ErrorReporter& reporter, const Module& module,
                        MethodDef& method);
    
  private:
    Resolver(ErrorReporter& reporter, const Module& module)
    : reporter_(reporter),
      module_(module),
      locals_()
    {}
    
    void resolve(gc<Expr> expr);
    
    virtual void visit(AndExpr& expr, int dummy);
    virtual void visit(BinaryOpExpr& expr, int dummy);
    virtual void visit(BoolExpr& expr, int dummy);
    virtual void visit(CallExpr& expr, int dummy);
    virtual void visit(CatchExpr& expr, int dummy);
    virtual void visit(DoExpr& expr, int dummy);
    virtual void visit(IfExpr& expr, int dummy);
    virtual void visit(IsExpr& expr, int dummy);
    virtual void visit(MatchExpr& expr, int dummy);
    virtual void visit(NameExpr& expr, int dummy);
    virtual void visit(NotExpr& expr, int dummy);
    virtual void visit(NothingExpr& expr, int dummy);
    virtual void visit(NumberExpr& expr, int dummy);
    virtual void visit(OrExpr& expr, int dummy);
    virtual void visit(RecordExpr& expr, int dummy);
    virtual void visit(ReturnExpr& expr, int dummy);
    virtual void visit(SequenceExpr& expr, int dummy);
    virtual void visit(StringExpr& expr, int dummy);
    virtual void visit(ThrowExpr& expr, int dummy);
    virtual void visit(VariableExpr& expr, int dummy);
        
    ErrorReporter& reporter_;

    const Module& module_;
    
    // The names of the current in-scope local variables (including all outer
    // scopes). The indices in this array correspond to the slots where those
    // locals are stored.
    Array<gc<String> > locals_;

    // The current inner-most local variable scope.
    Scope2* scope_;
    
    NO_COPY(Resolver);
  };
  
  // Keeps track of local variable scopes to handle nesting and shadowing.
  // This class can also visit patterns in order to create registers for the
  // variables declared in a pattern.
  class Scope2 : private PatternVisitor
  {
  public:
    Scope2(Resolver* resolver);      
    ~Scope2();
    
    void resolve(Pattern& pattern);
    
    // Creates a new local variable with the given name.
    int makeLocal(const SourcePos& pos, gc<String> name);
    
    // Closes this scope. This must be called before the Scope object goes out
    // of (C++) scope.
    void end();
    
    virtual void visit(RecordPattern& pattern, int dummy);
    virtual void visit(TypePattern& pattern, int dummy);
    virtual void visit(ValuePattern& pattern, int dummy);
    virtual void visit(VariablePattern& pattern, int dummy);
    virtual void visit(WildcardPattern& pattern, int dummy);
    
  private:
    Resolver& resolver_;
    Scope2* parent_;
    int start_;
    
    NO_COPY(Scope2);
  };
}