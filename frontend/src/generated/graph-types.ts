// Auto-generated from JSON Schema. DO NOT EDIT.
// Run `npm run generate-types` to regenerate.

export type ArrayOfInteger = number[];
export type ArrayOfString = string[];
export type Expression = Expression1 & Expression2;
export type Expression1 =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Path = (string | null)[];
export type Left =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Right =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Left1 =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Right1 =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Condition =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type ThenExpr =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type ElseExpr =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Predicate = (
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
) &
  (
    | (
        | {
            exprType?: 'constant';
            kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
            value: string;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'fieldAccess';
            path: Path;
            target?: string | null;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'binary';
            op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
            left: Left;
            right: Right;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'compare';
            op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
            left: Left1;
            right: Right1;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'if';
            condition: Condition;
            thenExpr: ThenExpr;
            elseExpr: ElseExpr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'aggregate';
            func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
            stateKey: string;
            itemName?: string;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            predicate?: Predicate;
            valueExpr?: ValueExpr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'not';
            expr: Expr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'call';
            function: string;
            args?: Args;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'bitset';
            op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
            args: Args1;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'fold';
            stateKey: string;
            accName: string;
            itemName: string;
            indexName?: string | null;
            init: Init;
            body: Body;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'map';
            stateKey: string;
            itemName: string;
            indexName?: string | null;
            body: Body1;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'filter';
            stateKey: string;
            itemName: string;
            indexName?: string | null;
            predicate: Predicate1;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
      )
    | ((
        | {
            exprType?: 'constant';
            kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
            value: string;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'fieldAccess';
            path: Path;
            target?: string | null;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'binary';
            op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
            left: Left;
            right: Right;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'compare';
            op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
            left: Left1;
            right: Right1;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'if';
            condition: Condition;
            thenExpr: ThenExpr;
            elseExpr: ElseExpr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'aggregate';
            func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
            stateKey: string;
            itemName?: string;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            predicate?: Predicate;
            valueExpr?: ValueExpr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'not';
            expr: Expr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'call';
            function: string;
            args?: Args;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'bitset';
            op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
            args: Args1;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'fold';
            stateKey: string;
            accName: string;
            itemName: string;
            indexName?: string | null;
            init: Init;
            body: Body;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'map';
            stateKey: string;
            itemName: string;
            indexName?: string | null;
            body: Body1;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'filter';
            stateKey: string;
            itemName: string;
            indexName?: string | null;
            predicate: Predicate1;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
      ) &
        null)
  );
export type ValueExpr = (
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
) &
  (
    | (
        | {
            exprType?: 'constant';
            kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
            value: string;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'fieldAccess';
            path: Path;
            target?: string | null;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'binary';
            op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
            left: Left;
            right: Right;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'compare';
            op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
            left: Left1;
            right: Right1;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'if';
            condition: Condition;
            thenExpr: ThenExpr;
            elseExpr: ElseExpr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'aggregate';
            func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
            stateKey: string;
            itemName?: string;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            predicate?: Predicate;
            valueExpr?: ValueExpr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'not';
            expr: Expr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'call';
            function: string;
            args?: Args;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'bitset';
            op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
            args: Args1;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'fold';
            stateKey: string;
            accName: string;
            itemName: string;
            indexName?: string | null;
            init: Init;
            body: Body;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'map';
            stateKey: string;
            itemName: string;
            indexName?: string | null;
            body: Body1;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'filter';
            stateKey: string;
            itemName: string;
            indexName?: string | null;
            predicate: Predicate1;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
      )
    | ((
        | {
            exprType?: 'constant';
            kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
            value: string;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'fieldAccess';
            path: Path;
            target?: string | null;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'binary';
            op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
            left: Left;
            right: Right;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'compare';
            op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
            left: Left1;
            right: Right1;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'if';
            condition: Condition;
            thenExpr: ThenExpr;
            elseExpr: ElseExpr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'aggregate';
            func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
            stateKey: string;
            itemName?: string;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            predicate?: Predicate;
            valueExpr?: ValueExpr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'not';
            expr: Expr;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'call';
            function: string;
            args?: Args;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'bitset';
            op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
            args: Args1;
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'fold';
            stateKey: string;
            accName: string;
            itemName: string;
            indexName?: string | null;
            init: Init;
            body: Body;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'map';
            stateKey: string;
            itemName: string;
            indexName?: string | null;
            body: Body1;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
        | {
            exprType?: 'filter';
            stateKey: string;
            itemName: string;
            indexName?: string | null;
            predicate: Predicate1;
            itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
            annotation?: string | null;
            [k: string]: unknown;
          }
      ) &
        null)
  );
export type Expr =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Args = Expression[];
export type Args1 = Expression[];
export type Init =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Body =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Body1 =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Predicate1 =
  | {
      exprType?: 'constant';
      kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
      value: string;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fieldAccess';
      path: Path;
      target?: string | null;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'binary';
      op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
      left: Left;
      right: Right;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'compare';
      op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
      left: Left1;
      right: Right1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'if';
      condition: Condition;
      thenExpr: ThenExpr;
      elseExpr: ElseExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'aggregate';
      func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
      stateKey: string;
      itemName?: string;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      predicate?: Predicate;
      valueExpr?: ValueExpr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'not';
      expr: Expr;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'call';
      function: string;
      args?: Args;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'bitset';
      op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
      args: Args1;
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'fold';
      stateKey: string;
      accName: string;
      itemName: string;
      indexName?: string | null;
      init: Init;
      body: Body;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'map';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      body: Body1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    }
  | {
      exprType?: 'filter';
      stateKey: string;
      itemName: string;
      indexName?: string | null;
      predicate: Predicate1;
      itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
      annotation?: string | null;
      [k: string]: unknown;
    };
export type Expression2 =
  | (
      | {
          exprType?: 'constant';
          kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
          value: string;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'fieldAccess';
          path: Path;
          target?: string | null;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'binary';
          op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
          left: Left;
          right: Right;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'compare';
          op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
          left: Left1;
          right: Right1;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'if';
          condition: Condition;
          thenExpr: ThenExpr;
          elseExpr: ElseExpr;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'aggregate';
          func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
          stateKey: string;
          itemName?: string;
          itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
          predicate?: Predicate;
          valueExpr?: ValueExpr;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'not';
          expr: Expr;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'call';
          function: string;
          args?: Args;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'bitset';
          op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
          args: Args1;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'fold';
          stateKey: string;
          accName: string;
          itemName: string;
          indexName?: string | null;
          init: Init;
          body: Body;
          itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'map';
          stateKey: string;
          itemName: string;
          indexName?: string | null;
          body: Body1;
          itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'filter';
          stateKey: string;
          itemName: string;
          indexName?: string | null;
          predicate: Predicate1;
          itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
          annotation?: string | null;
          [k: string]: unknown;
        }
    )
  | ((
      | {
          exprType?: 'constant';
          kind: 'Integer' | 'Rational' | 'Boolean' | 'String';
          value: string;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'fieldAccess';
          path: Path;
          target?: string | null;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'binary';
          op: 'Add' | 'Sub' | 'Mul' | 'Div' | 'And' | 'Or';
          left: Left;
          right: Right;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'compare';
          op: 'Eq' | 'Neq' | 'Lt' | 'Gt' | 'Lte' | 'Gte';
          left: Left1;
          right: Right1;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'if';
          condition: Condition;
          thenExpr: ThenExpr;
          elseExpr: ElseExpr;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'aggregate';
          func: 'Sum' | 'Product' | 'Count' | 'Min' | 'Max';
          stateKey: string;
          itemName?: string;
          itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
          predicate?: Predicate;
          valueExpr?: ValueExpr;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'not';
          expr: Expr;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'call';
          function: string;
          args?: Args;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'bitset';
          op: 'Union' | 'Intersect' | 'Except' | 'Shift' | 'Count' | 'First';
          args: Args1;
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'fold';
          stateKey: string;
          accName: string;
          itemName: string;
          indexName?: string | null;
          init: Init;
          body: Body;
          itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'map';
          stateKey: string;
          itemName: string;
          indexName?: string | null;
          body: Body1;
          itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
          annotation?: string | null;
          [k: string]: unknown;
        }
      | {
          exprType?: 'filter';
          stateKey: string;
          itemName: string;
          indexName?: string | null;
          predicate: Predicate1;
          itemType?: 'Number' | 'Boolean' | 'String' | 'Weights' | 'Symbol' | 'Array' | 'Error' | 'Record' | 'Null';
          annotation?: string | null;
          [k: string]: unknown;
        }
    ) &
      null);
export type ArrayOfNode = {
  id: string;
  label?: null | string;
  inputs?: DictionaryOfStringAndPort;
  outputs?: DictionaryOfStringAndPort;
  [k: string]: unknown;
}[];
export type ArrayOfEdge = {
  id: string;
  sourceNodeId: string;
  sourcePort: string;
  targetNodeId: string;
  targetPort: string;
  [k: string]: unknown;
}[];
export type ArrayOfPluginReference = {
  pluginId: string;
  contract: 'IEvaluator' | 'ITransform' | 'WeightSource';
  version?: null | string;
  config?: null | {
    [k: string]: string;
  };
  [k: string]: unknown;
}[];

/**
 * Canonical schema for slot math graph configuration.
 */
export interface SlotMathLabGraphSchema {
  schemaVersion: string;
  id?: null | string;
  name?: null | string;
  description?: null | string;
  evidenceInputs?:
    | null
    | {
        kind?: string;
        id?: string;
        version?: string;
        sha256?: string;
        uri?: null | string;
        contentBase64?: null | string;
        [k: string]: unknown;
      }[];
  symbols?: {
    id: string;
    name: string;
    kind?: 'Standard' | 'Wild' | 'Scatter' | 'Bonus' | 'Multiplier' | 'Money' | 'Jackpot';
    properties?: null | {
      [k: string]: string;
    };
    [k: string]: unknown;
  }[];
  paytables?: {
    id: string;
    entries: {
      symbolId: string;
      counts: ArrayOfInteger;
      payouts: ArrayOfString;
      [k: string]: unknown;
    }[];
    [k: string]: unknown;
  }[];
  paylineSets?: {
    id: string;
    paylines: {
      positions: ArrayOfInteger;
      [k: string]: unknown;
    }[];
    [k: string]: unknown;
  }[];
  reelStrips?: {
    id: string;
    name: string;
    symbols: ArrayOfString;
    [k: string]: unknown;
  }[];
  reelSets?: {
    id: string;
    name: string;
    stripIds: ArrayOfString;
    [k: string]: unknown;
  }[];
  boardConfig?: null | {
    rows?: number;
    columns?: number;
    allowMultiSymbol?: boolean;
    allowEmpty?: boolean;
    allowLocked?: boolean;
    growable?: boolean;
    [k: string]: unknown;
  };
  initialState?: null | {
    [k: string]: JsonElement;
  };
  nodes?: ArrayOfNode;
  edges?: ArrayOfEdge;
  expressions?: null | {
    [k: string]: Expression;
  };
  stateSchema?: {
    name: string;
    type?: null | string;
    [k: string]: unknown;
  }[];
  mechanics?: null | {
    [k: string]: CustomMechanic;
  };
  plugins?: ArrayOfPluginReference;
  [k: string]: unknown;
}
export interface JsonElement {
  valueKind?: 'Undefined' | 'Object' | 'Array' | 'String' | 'Number' | 'True' | 'False' | 'Null';
  [k: string]: unknown;
}
export interface DictionaryOfStringAndPort {
  [k: string]: {
    name: string;
    type?: 'Board' | 'State' | 'Weights' | 'Wins' | 'Number' | 'Boolean' | 'String' | 'Symbol' | 'Trigger';
    defaultValue?: null | Expression;
    [k: string]: unknown;
  };
}
export interface CustomMechanic {
  initialState?: null | {
    [k: string]: JsonElement;
  };
  name: string;
  description?: null | string;
  nodes?: ArrayOfNode;
  edges?: ArrayOfEdge;
  expressions?: null | {
    [k: string]: Expression;
  };
  plugins?: ArrayOfPluginReference;
  [k: string]: unknown;
}
