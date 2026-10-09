/** Parse the editor's deterministic arithmetic/boolean subset into the backend AST.
 * Unsupported syntax is an error; user text is never sent as an expression ID.
 */
export type ExpressionAst = { exprType: string; [key: string]: unknown };
export function parseExpression(source: string): ExpressionAst {
  const tokens: string[] = [];
  const pattern = /\s*(\d+(?:\.\d+)?|true\b|false\b|[A-Za-z_][A-Za-z_0-9]*|"(?:[^"\\]|\\.)*"|'[^']*'|==|!=|<=|>=|&&|\|\||[+*/!<>()?:.[\]-])/gy;
  let offset = 0;
  while (offset < source.trimEnd().length) {
    pattern.lastIndex = offset;
    const match = pattern.exec(source);
    if (!match) throw new Error(`Invalid expression at character ${offset + 1}`);
    tokens.push(match[1]); offset = pattern.lastIndex;
    if (tokens.length > 1000) throw new Error('Expression is too long');
  }
  let pos = 0;
  let depth = 0;
  const constant = (kind: string, value: string): ExpressionAst => ({ exprType: 'constant', kind, value });
  const take = (expected: string) => {
    if (tokens[pos++] !== expected) throw new Error(`Expected '${expected}' at token ${pos}`);
  };
  const operators: Record<string, [number, string, string]> = {
    '||': [1, 'binary', 'Or'], '&&': [2, 'binary', 'And'],
    '==': [3, 'compare', 'Eq'], '!=': [3, 'compare', 'Neq'],
    '<': [4, 'compare', 'Lt'], '>': [4, 'compare', 'Gt'], '<=': [4, 'compare', 'Lte'], '>=': [4, 'compare', 'Gte'],
    '+': [5, 'binary', 'Add'], '-': [5, 'binary', 'Sub'], '*': [6, 'binary', 'Mul'], '/': [6, 'binary', 'Div'],
  };
  function expression(min = 0): ExpressionAst {
    if (++depth > 64) throw new Error('Expression nesting exceeds 64');
    const token = tokens[pos++];
    let left: ExpressionAst;
    if (token === '(') { left = expression(); take(')'); }
    else if (token === '!') left = { exprType: 'not', expr: expression(7) };
    else if (token === '-') left = { exprType: 'binary', op: 'Sub', left: constant('Integer', '0'), right: expression(7) };
    else if (token === 'true' || token === 'false') left = constant('Boolean', token);
    else if (token && /^\d/.test(token)) {
      const [whole, fraction] = token.split('.');
      left = fraction ? constant('Rational', `${BigInt(whole + fraction)}/${10n ** BigInt(fraction.length)}`) : constant('Integer', token);
    } else if (token?.startsWith('"')) left = constant('String', JSON.parse(token));
    else if (token?.startsWith("'")) left = constant('String', token.slice(1, -1));
    else if (token && /^[A-Za-z_]/.test(token)) {
      const settlement = token === 'measurement' && tokens[pos] === '.';
      const path = token === 'state' || settlement ? [] : [token];
      while (tokens[pos] === '.' || tokens[pos] === '[') {
        const access = tokens[pos++]; const key = tokens[pos++];
        if (!key) throw new Error('Missing state field');
        path.push(key.startsWith('"') ? JSON.parse(key) : key.replace(/^'|'$/g, ''));
        if (access === '[') take(']');
      }
      if (!path.length) throw new Error('Select a state field');
      left = { exprType: 'fieldAccess', target: settlement ? 'measurement' : 'state', path };
    } else throw new Error(`Expected expression at token ${pos}`);
    while (operators[tokens[pos]] && operators[tokens[pos]][0] >= min) {
      const [precedence, exprType, op] = operators[tokens[pos++]];
      left = { exprType, op, left, right: expression(precedence + 1) };
    }
    if (min === 0 && tokens[pos] === '?') {
      pos++; const thenExpr = expression(); take(':');
      left = { exprType: 'if', condition: left, thenExpr, elseExpr: expression() };
    }
    depth--; return left;
  }
  const result = expression();
  if (pos !== tokens.length) throw new Error(`Unexpected '${tokens[pos]}' at token ${pos + 1}`);
  return result;
}
