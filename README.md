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
4. Invoke the `DoParse` method, which returns either an abstract syntax tree (AST) or an error. Use the code below:
   ```csharp
   Error<AST, string> result = DoParse(MainGrammarRule, input);
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
