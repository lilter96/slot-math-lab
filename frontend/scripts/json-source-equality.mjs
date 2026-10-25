/** Ignore layout while preserving exact numeric tokens, strings, types and array order. */
export function sameJsonSource(actual, expected) {
    const compact = source => {
        JSON.parse(source); // Reject malformed tokens before removing layout whitespace.
        return source.replace(/"(?:[^"\\]|\\.)*"|\s+/g, token => token.startsWith('"') ? token : '');
    };
    return compact(actual) === compact(expected);
}
