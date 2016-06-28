# MParse
MParse is a monadic parser for c#.

### Quickstart
1. Download the latest version from the Releases page.
2. Add the following to your `using` statements:
   ```csharp
   using ParseState = AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>;
   using NonTerminal = System.Func<AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>,
                                   AlgebraicTypes.Error<System.Tuple<string, string, System.Collections.Immutable.ImmutableList<AlgebraicTypes.Either<string, int>>>, string>>;
   using ASTMap = System.Collections.Generic.Dictionary<System.Tuple<string, int>, System.Collections.Generic.List<MParse.TermSpecification>>;
   using T = MParse.TermSpecification;
   
   using static MParse.MParse;
   ```
3. To transform your BNF grammar into C# code, follow the steps in the tutorial. (The tutorial is unwritten at the moment, but an example is povided below.)
4. Add a 'map' to tell the parser how to interpret the rules. (Again, see the tutorial, but there is an example below.)
5. Invoke the `DoParse` method, which returns either an abstract syntax tree (AST) or an error. Use the code below:
```csharp
Error<AST, string> result = DoParse(MainGrammarRule, input, map);
result.Match
(
  Result: ast =>
  {
    // Do something with the AST
  },
  Error: e =>
  {
    // Do something with the error, which is a human-friendly string stored in 'e'
  }
);
```
## Example
*BNF Grammar:*
```
<start> ::= <statement> ";" <start> | <statement> ";" ""
<statement> ::= <increment> | <decrement> | <assignment>
<increment> ::= <ID> "++"
<decrement> ::= <ID> "--"
<assignment> ::= <ID> "=" <ID> | <ID> = <literal>
<literal> ::= <intliteral> | <stringliteral>
(Assume that ID, intliteral and stringliteral are defined using the regexes '[_a-zA-Z](?:[a-zA-Z0-9]*)', '\d+' and '".*"' respectively)
```

*C# code:*
```csharp
static void Main(string[] args)
{
   ASTMap map = new ASTMap
   {
         [Specifier(nameof(Start), 0)] = new List<T> { T.NonTerminal(1), T.Terminal(";"), T.Option(0, -1) },
         [Specifier(nameof(Statement), 1)] = new List<T> { T.Option(2, 3, 4) },
         [Specifier(nameof(Increment), 2)] = new List<T> { T.NonTerminal(7), T.Terminal("++") },
         [Specifier(nameof(Decrement), 3)] = new List<T> { T.NonTerminal(7), T.Terminal("--") },
         [Specifier(nameof(Assignment), 4)] = new List<T> { T.Option(5, 6) },
         [Specifier(nameof(Assignment1), 5)] = new List<T> { T.NonTerminal(7), T.Terminal("="), T.NonTerminal(7) },
         [Specifier(nameof(Assignment1), 6)] = new List<T> { T.NonTerminal(7), T.Terminal("="), T.NonTerminal(10) },
         [Specifier(nameof(ID), 7)] = new List<T> { T.Base() },
         [Specifier(nameof(Literal), 8)] = new List<T> { T.Option(9, 10) },
         [Specifier(nameof(IntLiteral), 9)] = new List<T> { T.Base() },
         [Specifier(nameof(StringLiteral), 10)] = new List<T> { T.Base() }
   };
   map.Initialise();
   Error<AST, string> result = DoParse(Start, Console.ReadLine(), map);
   result.Match
   (
     Result: ast =>
     {
       // Do something with the AST
     },
     Error: e =>
     {
       // Do something with the error, which is a human-friendly string stored in 'e'
     }
   );
}

static ParseState Start(ParseState text) => text.Parse(Statement).Parse(';').Parse(Option(NewOption(Start, "statement"), NewOption(epsilon, "epsilon"))).Rule(0);

static NonTerminal Statement => Option(NewOption(Increment, "increment"), NewOption(Decrement, "decrement"), NewOption(Assignment, "assignment")).Rule(1);

static ParseState Increment(ParseState text) => text.Parse(ID).Parse("++").Rule(2);

static ParseState Decrement(ParseState text) => text.Parse(ID).Parse("--").Rule(3);

static NonTerminal Assignment => Option(NewOption(Assignment1, "assignment"), NewOption(Assignment2, "assignment")).Rule(4);
static ParseState Assignment1(ParseState text) => text.Parse(ID).Parse('=').Parse(ID).Rule(5);
static ParseState Assignment2(ParseState text) => text.Parse(ID).Parse('=').Parse(Literal).Rule(6);

static ParseState ID(ParseState text) => text.ParseRegex(@"[_a-zA-Z](?:[a-zA-Z0-9]*)", "identifier").Rule(7);

static NonTerminal Literal => Option(NewOption(IntLiteral, "integer literal"), NewOption(StringLiteral, "string literal")).Rule(8);
static ParseState IntLiteral(ParseState text) => text.ParseRegex(@"\d+", "digit").Rule(9);
static ParseState StringLiteral(ParseState text) => text.ParseRegex(@""".*""", "string literal").Rule(10);
```
