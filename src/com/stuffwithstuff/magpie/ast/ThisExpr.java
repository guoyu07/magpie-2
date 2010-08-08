package com.stuffwithstuff.magpie.ast;

public class ThisExpr extends Expr {
  public ThisExpr(Position position) {
    super(position);
  }
  
  @Override
  public <R, C> R accept(ExprVisitor<R, C> visitor, C context) {
    return visitor.visit(this, context);
  }

  @Override public String toString() { return "this"; }
}