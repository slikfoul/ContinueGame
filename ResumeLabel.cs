using System;
using System.Globalization;
using System.Text;

namespace ContinueGame
{
    internal static class ResumeLabel
    {
        // Keep full names; remove only line-breaking/control whitespace for menu presentation.
        internal static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var result = new StringBuilder();
            bool space = false;
            var elements = StringInfo.GetTextElementEnumerator(name);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                if (char.IsWhiteSpace(element, 0) || char.IsControl(element, 0))
                {
                    space = result.Length > 0;
                    continue;
                }
                if (space) result.Append(' ');
                result.Append(element);
                space = false;
            }
            return result.Length == 0 ? null : result.ToString();
        }

        internal static string Details(SessionRecord session, string characterName)
        {
            if (session == null) return "";
            string destination = Normalize(session.IsWorld ? session.WorldName : session.ServerName);
            string character = Normalize(characterName) ?? Normalize(session.CharacterName);
            string prefix = session.IsHostedWorld ? "Local server: " : session.IsLocalWorld ? "Local: " : "Server: ";
            string first = destination == null ? "" : prefix + destination;
            string second = character == null ? "" : "Character: " + character;
            return first.Length == 0 ? second : second.Length == 0 ? first : first + "\n" + second;
        }

        internal static string ServerName(SessionRecord current, SessionRecord previous, string discovered)
        {
            return Normalize(discovered) ?? Normalize(current.ServerName) ??
                (current.MatchesServer(previous) ? Normalize(previous.ServerName) : null);
        }
    }
}
