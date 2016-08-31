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
            throw new NotImplementedException();
        }
    }

    public class DFA
    {
        public Dictionary<int, Dictionary<char, int>> StateTable { get; set; }
        public int StartingState { get; set; }
        public List<int> AcceptingStates { get; set; }
        public DFA(Dictionary<int, Dictionary<char, int>> stateTable, List<int> acceptingStates, int startingState = 0)
        {
            StateTable = stateTable;
            StartingState = startingState;
            AcceptingStates = acceptingStates;
        }
    }

    public class NFA
    {
        public Dictionary<int, List<KeyValuePair<char, int>>> StateTable { get; set; }
        public int StartingState { get; set; }
        public List<int> AcceptingStates { get; set; }
        public NFA(Dictionary<int, List<KeyValuePair<char, int>>> stateTable, List<int> acceptingStates, int startingState = 0)
        {
            StateTable = stateTable;
            StartingState = startingState;
            AcceptingStates = acceptingStates;
        }
    }

    public class eNFA
    {
        public Dictionary<int, List<KeyValuePair<Maybe<char>, int>>> StateTable { get; set; }
        public int StartingState { get; set; }
        public List<int> AcceptingStates { get; set; }
        public eNFA(Dictionary<int, List<KeyValuePair<Maybe<char>, int>>> stateTable, List<int> acceptingStates, int startingState = 0)
        {
            StateTable = stateTable;
            StartingState = startingState;
            AcceptingStates = acceptingStates;
        }
        public NFA Close()
        {
            NFA nfa = new NFA(new Dictionary<int, List<KeyValuePair<char, int>>>(), this.AcceptingStates, this.StartingState);
            foreach (KeyValuePair<int, List<KeyValuePair<Maybe<char>, int>>> state in StateTable)
            {
                List<int> closure = CloseState(state.Key, new List<int> { state.Key }.ToImmutableList());
                List<KeyValuePair<char, int>> transitions = new List<KeyValuePair<char, int>>();
                foreach (int _state in closure)
                {
                    transitions.AddRange(StateTable[_state].Where(kvp => kvp.Key.State == MaybeState.Just)
                                                           .Select(kvp =>
                                                                new KeyValuePair<char, int>(
                                                                    kvp.Key.Match(Just: c => c,
                                                                                  Nothing: () => { throw new Exception(); }),
                                                                    kvp.Value)));
                }
                nfa.StateTable.Add(state.Key, transitions);
            }
            return nfa;
        }
        private List<int> CloseState(int state, ImmutableList<int> alreadyVisitedStates)
        {
            List<int> epsilonReachableStates = (from kvp in StateTable[state]
                                                where kvp.Key.State == MaybeState.Nothing
                                                where !alreadyVisitedStates.Contains(kvp.Value)
                                                select kvp.Value).ToList();
            return epsilonReachableStates.SelectMany(s => CloseState(s,
                alreadyVisitedStates.AddRange(epsilonReachableStates))).ToImmutableList().Add(state).ToList();
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
