-- corpus-string-functions.cypher
-- String function golden coverage: trim, pad, repeat, reverse, substring, replace, split, regex.

-- QUERY: trim_variants
RETURN trim('  hello  ') AS trimmed,
       ltrim('  hello  ') AS ltrimmed,
       rtrim('  hello  ') AS rtrimmed;

-- QUERY: pad_functions
RETURN lpad('abc', 6, '0') AS left_padded,
       rpad('abc', 6, '!') AS right_padded;

-- QUERY: repeat_and_reverse
RETURN repeat('ab', 3) AS rep,
       reverse('hello') AS rev;

-- QUERY: substring_variants
RETURN substring('abcdefgh', 3, 4) AS mid,
       left('abcdefgh', 3) AS lft,
       right('abcdefgh', 3) AS rgt;

-- QUERY: replace_and_concat
RETURN replace('hello world', 'world', 'bogdb') AS replaced,
       concat('foo', 'bar', 'baz') AS joined;

-- QUERY: upper_lower_size
RETURN upper('Hello World') AS up,
       lower('Hello World') AS lo,
       size('Hello World') AS sz;

-- QUERY: starts_ends_contains
RETURN starts_with('foobar', 'foo') AS sw,
       ends_with('foobar', 'bar') AS ew,
       contains('foobar', 'oba') AS ct;

-- QUERY: split_and_index
RETURN list_element(string_split('a,b,c,d', ','), 2) AS second_token,
       array_length(string_split('a,b,c,d', ',')) AS token_count;

-- QUERY: regex_extract
RETURN regexp_extract('abc_123_def', '[0-9]+') AS digits;

-- QUERY: position_and_instr
RETURN strpos('hello world', 'world') AS pos;
