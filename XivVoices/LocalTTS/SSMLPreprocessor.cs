using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XivVoices.LocalTTS
{
    public struct SpeechUnit
    {
        public string Text;
    }
    
    public static class SSMLPreprocessor
    {
        public static SpeechUnit[] Preprocess(string ssml)
        {
            if (ssml.Length == 0) return Array.Empty<SpeechUnit>();
            var speechUnits = new List<SpeechUnit>();
            var currentUnit = new StringBuilder();
            
            using (var reader = new StringReader(ssml))
            {
                while(reader.Peek() != -1)
                {
                    var nextChar = (char)reader.Read();
                    
                    if (nextChar == '<')
                    {
                        var tagBuilder = new StringBuilder();
                        while(reader.Peek() != -1 && (nextChar = (char)reader.Read()) != '>')
                        {
                            tagBuilder.Append(nextChar);
                        }
                        
                        var tag = tagBuilder.ToString();
                        
                        if (tag.StartsWith("break"))
                        {
                            currentUnit.AppendLine();
                            currentUnit.AppendLine();
                        }
                        else if (tag.StartsWith("prosody"))
                        {
                            speechUnits.Add(new SpeechUnit { Text = currentUnit.ToString() });
                            currentUnit.Clear();
                        }
                    }
                    else
                    {
                        currentUnit.Append(nextChar);
                    }
                }
            }
            
            if (currentUnit.Length > 0)
            {
                speechUnits.Add(new SpeechUnit { Text = currentUnit.ToString() });
            }

            return speechUnits.ToArray();
        }
    }
}