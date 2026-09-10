using System.Text;

namespace MorseDecoder.Core.Dsp;

public static class MorseCodec
{
    private static readonly IReadOnlyDictionary<string, string> CodeToText = new Dictionary<string, string>
    {
        [".-"] = "A",
        ["-..."] = "B",
        ["-.-."] = "C",
        ["-.."] = "D",
        ["."] = "E",
        ["..-."] = "F",
        ["--."] = "G",
        ["...."] = "H",
        [".."] = "I",
        [".---"] = "J",
        ["-.-"] = "K",
        [".-.."] = "L",
        ["--"] = "M",
        ["-."] = "N",
        ["---"] = "O",
        [".--."] = "P",
        ["--.-"] = "Q",
        [".-."] = "R",
        ["..."] = "S",
        ["-"] = "T",
        ["..-"] = "U",
        ["...-"] = "V",
        [".--"] = "W",
        ["-..-"] = "X",
        ["-.--"] = "Y",
        ["--.."] = "Z",
        ["-----"] = "0",
        [".----"] = "1",
        ["..---"] = "2",
        ["...--"] = "3",
        ["....-"] = "4",
        ["....."] = "5",
        ["-...."] = "6",
        ["--..."] = "7",
        ["---.."] = "8",
        ["----."] = "9",
        [".-.-.-"] = ".",
        ["--..--"] = ",",
        ["..--.."] = "?",
        [".----."] = "'",
        ["-.-.--"] = "!",
        ["-..-."] = "/",
        ["-.--."] = "(",
        ["-.--.-"] = ")",
        [".-..."] = "&",
        ["---..."] = ":",
        ["-.-.-."] = ";",
        ["-...-"] = "=",
        [".-.-."] = "+",
        ["-....-"] = "-",
        ["..--.-"] = "_",
        [".-..-."] = "\"",
        ["...-..-"] = "$",
        [".--.-."] = "@",
        ["...-.-"] = "[SK]"
    };

    private static readonly IReadOnlyDictionary<char, string> TextToCode = BuildTextToCode();

    public static string Decode(string code)
    {
        return CodeToText.TryGetValue(code, out var text) ? text : "?";
    }

    public static bool TryEncode(char character, out string code)
    {
        return TextToCode.TryGetValue(char.ToUpperInvariant(character), out code!);
    }

    private static IReadOnlyDictionary<char, string> BuildTextToCode()
    {
        var result = new Dictionary<char, string>();
        foreach (var pair in CodeToText)
        {
            if (pair.Value.Length != 1 || pair.Value[0] == '?')
            {
                continue;
            }

            result.TryAdd(pair.Value[0], pair.Key);
        }

        return result;
    }
}
