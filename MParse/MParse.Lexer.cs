using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CSFunc.Types;

namespace MParse.Lexer
{
    public class Lexer
    {
        public ImmutableList<KeyValuePair<string, int>> TokenSpecifications { get; private set; }
        public Lexer(params KeyValuePair<string, int>[] tokenSpecifications)
        {
        }
        public Error<ImmutableList<Token>, LexerError> Lex(string input, Func<int, int, ILocation> locator)
        {
        }
    }

    public struct Token
    {
        public int Type { get; }
        public string Value { get; }
        public ILocation Location { get; }
        public Token(int type, string value, ILocation location)
        {
            Type = type;
            Value = value;
            Location = location;
        }

        public override string ToString()
        {
            return $"Token({Type}, {Value}, {Location.ToString()})";
        }
    }

    public interface ILocation { }

    public class TokenError
    {
        public ILocation Location { get; }
        public TokenError(ILocation location)
        {
            Location = location;
        }
    }

    public class LexerError : TokenError
    {
        public char Next { get; }
        public LexerError(char next, ILocation location) : base(location)
        {
            Next = next;
        }
        public string ToString(bool putPositionAtFront = true)
        {
            string result = "";
            if (putPositionAtFront) result += $"{Location.ToString()} ";
            result += $"Error: Unexpected character {Next}";
            if (!putPositionAtFront) result += $"at {Location.ToString()}";
            return result;
        }
    }
}
