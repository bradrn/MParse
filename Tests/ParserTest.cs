﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CSFunc.Types;
using MParse.Parser;
using MParse.Lexer;

// Using statements required for MParse.Parser
using ParseState = CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>;
using NonTerminal = System.Func<CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>,
                                CSFunc.Types.Error<System.Tuple<System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, System.Collections.Immutable.ImmutableList<MParse.Lexer.Token>, MParse.Parser.Tree<MParse.Parser.Term>>, MParse.Parser.ParseError>>;
using AST = MParse.Parser.Tree<MParse.Parser.Term>;
using TSpec = System.Collections.Generic.KeyValuePair<string, int>;

namespace Tests
{
    [TestClass]
    public class ParserTest
    {
        [TestMethod]
        public void Parse_SingleToken_Success()
        {
            NonTerminal nt = text => text.Parse(0).Rule(0);
            Error<AST, ParseError> result = Parser.DoParse(nt, new List<Token> { new Token(0, "token", new SampleLocation()) }.ToImmutableList());
            Assert.IsTrue(result.State == ErrorState.Result);
            Assert.IsTrue(result.Match(Result: ast => ast.Value.Match(Terminal: tok => false,
                                                                      NonTerminal: _nt => _nt == 0,
                                                                      Loop: l => false,
                                                                      EndLoop: () => false), Throw: perr => false));
            Assert.IsTrue(result.Match(Result: ast => ast.Children[0].Value.Match(Terminal: tok => tok.Value == "token" && tok.Type == 0,
                                                                                  NonTerminal: _nt => false,
                                                                                  Loop: l => false,
                                                                                  EndLoop: () => false), Throw: perr => false));
        }

        class SampleLocation : ILocation { }
    }
}
