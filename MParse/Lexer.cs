using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AlgebraicTypes;

namespace MParse.Lexer
{
    public class Lexer
    {
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
    }

    public interface ILocation { }

    public class TokenError
    {
        public class ExpectedValue
        {
            // ExpectedValue = EOF | Token int | Option string[]
            private enum ExpectedValueState
            {
                EOF, Token, Option
            }
            private ExpectedValueState State { get; set; }
            private Unit EOFField;
            private Unit EOFValue { get { return EOFField; } set { EOFField = value; TokenField = 0; OptionField = null; State = ExpectedValueState.EOF; } }
            private int TokenField;
            private int TokenValue { get { return TokenField; } set { TokenField = value; EOFField = null; OptionField = null; State = ExpectedValueState.Token; } }
            private string[] OptionField;
            private string[] OptionValue { get { return OptionField; } set { OptionField = value; EOFField = null; TokenField = 0; State = ExpectedValueState.Option; } }
            private ExpectedValue() { }
            public static ExpectedValue EOF()
            {
                ExpectedValue result = new ExpectedValue();
                result.EOFValue = Unit.Nil;
                return result;
            }
            public static ExpectedValue Token(int value1)
            {
                ExpectedValue result = new ExpectedValue();
                result.TokenValue = value1;
                return result;
            }
            public static ExpectedValue Option(string[] value1)
            {
                ExpectedValue result = new ExpectedValue();
                result.OptionValue = value1;
                return result;
            }
            public T1 Match<T1>(Func<T1> EOF, Func<int, T1> Token, Func<string[], T1> Option)
            {
                switch (State)
                {
                    case ExpectedValueState.EOF: return EOF();
                    case ExpectedValueState.Token: return Token(TokenValue);
                    case ExpectedValueState.Option: return Option(OptionValue);
                }
                return default(T1);
            }
        }
        public class GotValue
        {
            // GotValue = EOF | Token Token | None
            private enum GotValueState
            {
                EOF, Token, None
            }
            private GotValueState State { get; set; }
            private Unit EOFField;
            private Unit EOFValue { get { return EOFField; } set { EOFField = value; TokenField = new Token(); NoneField = null; State = GotValueState.EOF; } }
            private Token TokenField;
            private Token TokenValue { get { return TokenField; } set { TokenField = value; EOFField = Unit.Nil; NoneField = null; State = GotValueState.Token; } }
            private Unit NoneField;
            private Unit NoneValue { get { return NoneField; } set { NoneField = value; TokenField = new Token(); EOFField = null; State = GotValueState.EOF; } }
            private GotValue() { }
            public static GotValue EOF()
            {
                GotValue result = new GotValue();
                result.EOFValue = Unit.Nil;
                return result;
            }
            public static GotValue Token(Token value1)
            {
                GotValue result = new GotValue();
                result.TokenValue = value1;
                return result;
            }
            public static GotValue None()
            {
                GotValue result = new GotValue();
                result.NoneValue = Unit.Nil;
                return result;
            }
            public T1 Match<T1>(Func<T1> EOF, Func<Token, T1> Token, Func<T1> None)
            {
                switch (State)
                {
                    case GotValueState.EOF: return EOF();
                    case GotValueState.Token: return Token(TokenValue);
                    case GotValueState.None: return None();
                }
                return default(T1);
            }
        }
        public ExpectedValue Expected { get; }
        public GotValue Got { get; }
        public ILocation Location { get; }
        public TokenError(ExpectedValue expected, GotValue got, ILocation location)
        {
            Expected = expected;
            Got = got;
            Location = location;
        }
        public string ToString(Dictionary<int, string> tokenMap, bool putPositionAtFront = true)
        {
            string value = "";
            if (putPositionAtFront) value += Location.ToString();
            value += "Error: Expected ";
            value += Expected.Match(EOF: () => "EOF",
                                    Token: t => tokenMap[t],
                                    Option: options =>
                                    {
                                        string description = "";
                                        for (int i = 0; i < options.Count(); i++)
                                        {
                                            if (i != options.Count() - 1) description += options[i] + ", ";
                                            else description += "or " + options[i];
                                        }
                                        return description;
                                    });
            value += Got.Match(EOF: () => ", but got EOF",
                               Token: t => ", but got " + tokenMap[t.Type],
                               None: () => ""
                              );
            if (!putPositionAtFront) value += " at " + Location.ToString();
            return value;
        }
    }

    public class eNFA
    {
        private Dictionary<char, int> colmap = new Dictionary<char, int>();
        private int[,] table;

        public eNFA(string[] columns, int[,] table)
        {
            if (columns.Length != table.GetLength(1)) throw new ArgumentException("Column numbers did not match up");
            this.table = table;
            colmap = columns.SelectMany((s, i) => s.Select(c => new KeyValuePair<char, int>(c, i)))
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public int this[int state, char input]
        {
            get { return table[state, colmap[input]]; }
            set { table[state, colmap[input]] = value; }
        }

        public int[,] GetTable() => table;
    }
}
