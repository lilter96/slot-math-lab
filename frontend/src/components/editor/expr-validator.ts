/**
 * Frontend expression validator that mirrors the backend type-checker (G11).
 * Handles arithmetic, boolean, comparison, conditional, and board aggregations.
 * No loops/recursion — expressions are pure, total, deterministic.
 */

export type ExprType = 'Number' | 'Boolean';

export interface ExprContext {
  symbols?: { id: string; kind: string }[];
  board?: { rows: number; columns: number };
  state?: Record<string, ExprType | Record<string, ExprType>>;
}

export interface AnalysisResult {
  ok: boolean;
  type: ExprType | 'unknown';
  errors: { msg: string; pos?: number }[];
  preview?: string;
}

// ── Tokenizer ───────────────────────────────────────────────────────
interface Token {
  type: 'num' | 'ident' | 'op' | 'kw' | 'paren' | 'comma' | 'eof' | 'err';
  value: string;
  start: number;
  end: number;
}

function tokenize(src: string): Token[] {
  const toks: Token[] = [];
  let i = 0;
  while (i < src.length) {
    const ch = src[i];
    if (/\s/.test(ch)) { i++; continue; }
    if (/[0-9]/.test(ch) || (ch === '.' && i + 1 < src.length && /[0-9]/.test(src[i + 1]))) {
      const start = i;
      while (i < src.length && /[0-9.]/.test(src[i])) i++;
      toks.push({ type: 'num', value: src.slice(start, i), start, end: i });
      continue;
    }
    if (/[A-Za-z_]/.test(ch)) {
      const start = i;
      while (i < src.length && /[A-Za-z0-9_.]/.test(src[i])) i++;
      const word = src.slice(start, i);
      const kw = ['and', 'or', 'not', 'if', 'sum', 'product', 'count', 'min', 'max', 'true', 'false'];
      if (kw.includes(word)) {
        toks.push({ type: 'kw', value: word, start, end: i });
      } else if (word === 'board' || word === 'state') {
        toks.push({ type: 'ident', value: word, start, end: i });
      } else {
        toks.push({ type: 'ident', value: word, start, end: i });
      }
      continue;
    }
    if ('()[],'.includes(ch)) {
      if (ch === '(' || ch === ')') toks.push({ type: 'paren', value: ch, start: i, end: i + 1 });
      else toks.push({ type: 'comma', value: ch, start: i, end: i + 1 });
      i++; continue;
    }
    if ('+-*/^=<>!'.includes(ch)) {
      const start = i;
      if ('=!<>'.includes(ch) && i + 1 < src.length && src[i + 1] === '=') i++;
      i++;
      toks.push({ type: 'op', value: src.slice(start, i), start, end: i });
      continue;
    }
    toks.push({ type: 'err', value: ch, start: i, end: i + 1 });
    i++;
  }
  toks.push({ type: 'eof', value: '', start: src.length, end: src.length });
  return toks;
}

// ── Parser / Type checker ────────────────────────────────────────────
const FUNCS: Record<string, { returnType: ExprType; argTypes: ExprType[] }> = {
  sum: { returnType: 'Number', argTypes: ['Number'] },
  product: { returnType: 'Number', argTypes: ['Number'] },
  count: { returnType: 'Number', argTypes: [] },
  min: { returnType: 'Number', argTypes: ['Number'] },
  max: { returnType: 'Number', argTypes: ['Number'] },
};

function fieldType(field: string, ctx: ExprContext): ExprType | 'unknown' {
  if (field.startsWith('board.')) {
    const rest = field.slice(6);
    if (rest === 'rows' || rest === 'columns' || rest === 'symbolCount' ||
        rest.includes('multiplier') || rest.includes('value')) return 'Number';
    if (rest.includes('kind') || rest.includes('has')) return 'Boolean';
    return 'Number';
  }
  if (field.startsWith('state.')) {
    const path = field.slice(6).split('.');
    let curr: unknown = ctx.state;
    for (const p of path) {
      if (curr && typeof curr === 'object' && p in curr) {
        curr = (curr as Record<string, unknown>)[p];
      } else return 'unknown';
    }
    if (typeof curr === 'string') return curr === 'Number' ? 'Number' : 'Boolean';
    return 'Number';
  }
  if (field === 'true' || field === 'false') return 'Boolean';
  // Symbol IDs evaluate to Number (multiplier value)
  if (ctx.symbols?.some((s) => s.id === field)) return 'Number';
  return 'unknown';
}

class Parser {
  toks: Token[];
  pos = 0;
  errors: { msg: string; pos?: number }[] = [];
  ctx: ExprContext;

  constructor(tokens: Token[], ctx: ExprContext) {
    this.toks = tokens;
    this.ctx = ctx;
  }

  cur() { return this.toks[this.pos]; }
  eat(type?: string, value?: string) {
    const t = this.toks[this.pos];
    if (type && t.type !== type) return null;
    if (value && t.value !== value) return null;
    this.pos++;
    return t;
  }
  expect(type: string, msg: string): Token | null {
    const t = this.eat(type);
    if (!t) this.errors.push({ msg, pos: this.cur().start });
    return t;
  }

  // expression -> comparison
  parseExpr(expected?: ExprType): { type: ExprType | 'unknown'; ok: boolean } {
    const left = this.parseComparison();
    if (!left.ok) return left;
    if (expected && left.type !== expected && left.type !== 'unknown') {
      this.errors.push({ msg: `Expected ${expected}, got ${left.type}`, pos: 0 });
      return { type: left.type, ok: false };
    }
    return left;
  }

  // comparison -> term ((==|!=|<|>|<=|>=) term)?
  parseComparison(): { type: ExprType | 'unknown'; ok: boolean } {
    const left = this.parseAdditive();
    if (!left.ok) return left;
    const op = this.eat('op');
    if (op && ['==', '!=', '<', '>', '<=', '>='].includes(op.value)) {
      const right = this.parseAdditive();
      if (!right.ok) return right;
      return { type: 'Boolean', ok: true };
    }
    if (op) this.pos--; // backtrack
    return left;
  }

  // additive -> multiplicative ((+|-) multiplicative)*
  parseAdditive(): { type: ExprType | 'unknown'; ok: boolean } {
    const left = this.parseMultiplicative();
    if (!left.ok) return left;
    while (this.cur().type === 'op' && (this.cur().value === '+' || this.cur().value === '-')) {
      this.pos++;
      const right = this.parseMultiplicative();
      if (!right.ok) return right;
      if (left.type !== 'Number' || right.type !== 'Number') {
        this.errors.push({ msg: 'Addition/subtraction requires Number operands' });
        return { type: 'Number', ok: false };
      }
    }
    return left;
  }

  // multiplicative -> unary ((*|/|^) unary)*
  parseMultiplicative(): { type: ExprType | 'unknown'; ok: boolean } {
    const left = this.parseUnary();
    if (!left.ok) return left;
    while (this.cur().type === 'op' && ['*', '/', '^'].includes(this.cur().value)) {
      this.pos++;
      const right = this.parseUnary();
      if (!right.ok) return right;
      if (left.type !== 'Number' || right.type !== 'Number') {
        this.errors.push({ msg: 'Multiplication/division requires Number operands' });
        return { type: 'Number', ok: false };
      }
    }
    return left;
  }

  // unary -> - factor | not factor | factor
  parseUnary(): { type: ExprType | 'unknown'; ok: boolean } {
    if (this.eat('op', '-')) {
      const r = this.parseAtom();
      return { type: 'Number', ok: r.ok };
    }
    if (this.eat('kw', 'not')) {
      const r = this.parseAtom();
      return { type: 'Boolean', ok: r.ok && r.type !== 'Number' };
    }
    return this.parseAtom();
  }

  // atom -> num | ident | ident(args) | if(expr, expr, expr) | (expr) | board[...]
  parseAtom(): { type: ExprType | 'unknown'; ok: boolean } {
    const t = this.cur();

    // Number literal
    if (t.type === 'num') {
      this.pos++;
      return { type: 'Number', ok: true };
    }

    // Boolean literal
    if (t.type === 'kw' && (t.value === 'true' || t.value === 'false')) {
      this.pos++;
      return { type: 'Boolean', ok: true };
    }

    // Parenthesized expression
    if (this.eat('paren', '(')) {
      const r = this.parseExpr();
      this.expect('paren', 'Expected closing )');
      if (this.cur().value === ')') this.pos++;
      return r;
    }

    // if(cond, then, else)
    if (this.eat('kw', 'if')) {
      this.expect('paren', 'Expected ( after if');
      if (this.cur().value === '(') this.pos++;
      const cond = this.parseExpr();
      this.expect('comma', 'Expected , after condition');
      if (this.cur().value === ',') this.pos++;
      const thenVal = this.parseExpr();
      this.expect('comma', 'Expected , after then-branch');
      if (this.cur().value === ',') this.pos++;
      const elseVal = this.parseExpr();
      this.expect('paren', 'Expected ) after else-branch');
      if (this.cur().value === ')') this.pos++;
      if (cond.type !== 'Boolean' && cond.type !== 'unknown') {
        this.errors.push({ msg: 'if() condition must be Boolean' });
      }
      return { type: thenVal.type === elseVal.type ? thenVal.type : 'unknown', ok: cond.ok && thenVal.ok && elseVal.ok };
    }

    // Aggregation functions: sum(expr), product(expr), count(), min(expr), max(expr)
    if (t.type === 'kw' && FUNCS[t.value]) {
      const fnName = t.value;
      this.pos++;
      const fn = FUNCS[fnName];
      if (this.eat('paren', '(')) {
        if (fn.argTypes.length > 0) {
          this.parseExpr();
        }
        this.expect('paren', `Expected ) after ${fnName}`);
        if (this.cur().value === ')') this.pos++;
      }
      return { type: fn.returnType, ok: this.errors.length === 0 };
    }

    // Boolean functions: and/or — parsed as infix in comparison
    if (t.type === 'kw' && (t.value === 'and' || t.value === 'or')) {
      this.pos++;
      const right = this.parseComparison();
      return { type: 'Boolean', ok: right.ok };
    }

    // Board field access: board.something or board[row][col]
    if (t.type === 'ident' && t.value === 'board') {
      this.pos++;
      if (this.eat('op', '[') || this.eat('comma', '[')) {
        this.parseExpr(); // row
        if (this.cur().value === ']') this.pos++;
        if (this.eat('comma', '[') || this.eat('op', '[')) {
          this.parseExpr(); // col
          if (this.cur().value === ']') this.pos++;
        }
        return { type: 'Number', ok: true };
      }
      if (this.eat('op', '.')) {
        const field = this.eat('ident');
        if (field) {
          const ft = fieldType('board.' + field.value, this.ctx);
          return { type: ft === 'unknown' ? 'Number' : ft, ok: true };
        }
      }
      return { type: 'Number', ok: true };
    }

    // State field access
    if (t.type === 'ident' && t.value === 'state') {
      this.pos++;
      if (this.eat('op', '.')) {
        const field = this.eat('ident');
        if (field) {
          const ft = fieldType('state.' + field.value, this.ctx);
          return { type: ft === 'unknown' ? 'Number' : ft, ok: true };
        }
      }
      return { type: 'Number', ok: true };
    }

    // Identifier — could be symbol reference, field, or function
    if (t.type === 'ident') {
      const name = t.value;
      this.pos++;

      // Function call with args: name(arg1, arg2, ...)
      if (this.cur().value === '(') {
        this.pos++; // skip (
        while (this.cur().value !== ')' && this.cur().type !== 'eof') {
          this.parseExpr();
          if (this.cur().value === ',') this.pos++;
        }
        if (this.cur().value === ')') this.pos++;
        return { type: 'Number', ok: this.errors.length === 0 };
      }

      // Simple identifier — symbol reference or field
      const ft = fieldType(name, this.ctx);
      return { type: ft, ok: true };
    }

    this.errors.push({ msg: `Unexpected token: ${t.value}`, pos: t.start });
    return { type: 'unknown', ok: false };
  }
}

// ── Public API ──────────────────────────────────────────────────────
export function analyze(src: string, ctx: ExprContext = {}): AnalysisResult {
  if (!src || !src.trim()) {
    return { ok: true, type: 'unknown', errors: [] };
  }
  const toks = tokenize(src);
  const parser = new Parser(toks, ctx);
  const result = parser.parseExpr();
  return {
    ok: result.ok && parser.errors.length === 0,
    type: result.type,
    errors: parser.errors,
  };
}

/** Returns the list of known functions for autocomplete. */
export function getKnownFunctions(): { name: string; returnType: string; desc: string }[] {
  return [
    { name: 'sum', returnType: 'Number', desc: 'Sum values over the board' },
    { name: 'product', returnType: 'Number', desc: 'Product of values over the board' },
    { name: 'count', returnType: 'Number', desc: 'Count of matching symbols' },
    { name: 'min', returnType: 'Number', desc: 'Minimum value on the board' },
    { name: 'max', returnType: 'Number', desc: 'Maximum value on the board' },
    { name: 'if', returnType: 'varies', desc: 'Conditional: if(pred, then, else)' },
    { name: 'and', returnType: 'Boolean', desc: 'Logical AND' },
    { name: 'or', returnType: 'Boolean', desc: 'Logical OR' },
    { name: 'not', returnType: 'Boolean', desc: 'Logical NOT' },
  ];
}
