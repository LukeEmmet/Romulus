using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Romulus
{
    public static class Utils
    {

        public static string[] ParseGeminiLink(string line)
        {
            var linkRegex = new Regex(@"=>\s*([^\s]*)(.*)");
            string[] array = new string[2];

            if (linkRegex.IsMatch(line))
            {
                Match match = linkRegex.Match(line);
                array[0] = match.Groups[1].ToString().Trim();
                array[1] = match.Groups[2].ToString().Trim();

                //if display text is empty, use url
                if (array[1] == "")
                {
                    array[1] = array[0];
                }
            }
            else
            {
                //isnt a link, return null,null
                array[0] = null;
                array[1] = null;
            }

            return array;
        }



        //normalise tabs to spaces
        public static string TabsToSpaces(string start)
        {
            return start.Replace("\t", "    ");
        }

        /// <summary>
        /// from https://gist.github.com/anderssonjohan/660952
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxLineLength"></param>
        /// <returns></returns>
        /// 
        public static List<string> WordWrap(string text, int maxLineLength)
        {
            var list = new List<string>();

            int currentIndex;
            var lastWrap = 0;
            var whitespace = new[] { ' ', '\r', '\n', '\t' };
            do
            {
                currentIndex = lastWrap + maxLineLength > text.Length ? text.Length : (text.LastIndexOfAny(new[] { ' ', ',', '.', '?', '!', ':', ';', '-', '\n', '\r', '\t' }, Math.Min(text.Length - 1, lastWrap + maxLineLength)) + 1);
                if (currentIndex <= lastWrap)
                    currentIndex = Math.Min(lastWrap + maxLineLength, text.Length);
                list.Add(text.Substring(lastWrap, currentIndex - lastWrap).Trim(whitespace));
                lastWrap = currentIndex;
            } while (currentIndex < text.Length);

            return list;
        }
    }
}