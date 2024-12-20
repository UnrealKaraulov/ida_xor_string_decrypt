using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Text.RegularExpressions;

class Program
{
    static List<int> toClear = new List<int>();

    static List<byte[]> ExtractHexValues(string filename)
    {
        var hexValues = new List<byte[]>();
        var pattern = new Regex(@"0x[0-9A-Fa-f]+");

        foreach (var line in File.ReadLines(filename))
        {
            foreach (Match match in pattern.Matches(line))
            {
                byte[] val = BytesFromHex(match.Value);
                val = val.Reverse().ToArray();
                hexValues.Add(val);
            }
        }

        return hexValues;
    }

    static List<string> GenerateXorStrings(List<byte[]> hexValues)
    {
        var xorResults = new List<string>();

        while (hexValues.Count > 0)
        {
            byte[] baseValue = hexValues[0];
            hexValues.RemoveAt(0);

            string str1 = "";
            string str2 = "";
            int i = 0;
            for (int x = 0; x < 2; x++)
            {
                for (; i < Math.Min(20, hexValues.Count); i++)
                {
                    byte[] op1Bytes = baseValue;
                    byte[] op2Bytes = hexValues[i];

                    byte[] result = XorOperands(op1Bytes, op2Bytes);
                    string resultHex = BitConverter.ToString(result).Replace("-", "").ToUpper();
                    string hexString = HexToString(resultHex);

                    if (!ContainsNonPrintableChars(hexString) && hexString.Length > 1)
                    {
                        if (x == 0)
                        {
                            str1 = hexString + (resultHex.EndsWith("00") ? "\n" : "");
                        }
                        else
                        {
                            str2 = hexString + (resultHex.EndsWith("00") ? "\n" : "");
                        }
                        break;
                    }
                }
            }
            if (str1.Length > 0 || str2.Length > 0)
            {
                if (str1.Length > str1.Length)
                {
                    xorResults.Add(str1);
                }
                else
                {
                    xorResults.Add(str2);
                }
            }
        }

        return xorResults;
    }

    static bool ContainsNonPrintableChars(string input)
    {
        foreach (char c in input)
        {
            if (!char.IsLetter(c) && !char.IsDigit(c))
            {
                return true;
            }
        }
        return false;
    }

    static void SaveResultsToFile(List<string> results, string filename)
    {
        string tmpString = "";
        using (StreamWriter file = new StreamWriter(filename, false, System.Text.Encoding.UTF8))
        {
            foreach (var result in results)
            {
                tmpString += result;
                if (result.IndexOf('\n') != -1)
                {
                    if (tmpString.Length > 1)
                    {
                        file.Write(tmpString);
                    }
                    tmpString = "";
                }
            }
            if (tmpString.Length > 2)
            {
                file.Write(tmpString);
                tmpString = "";
            }
        }
        Console.WriteLine($"Results saved to {filename}");
    }

    static byte[] XorOperands(byte[] op1, byte[] op2)
    {
        int minLength = Math.Min(op1.Length, op2.Length);
        return op1.Take(minLength).Zip(op2.Take(minLength), (b1, b2) => (byte)(b1 ^ b2)).ToArray();
    }


    static ulong[] Extract_2x_ulong(string[] lines, string varName, int startLine, int searchLimit = 1000)
    {
        ulong[] values = new ulong[2] { 0, 0 };
        var pattern = new Regex($@".*?{Regex.Escape(varName)}\.m128_.64\[(\d+)\]\s*=\s*0x([0-9A-Fa-f]+)");
        for (int i = startLine; i >= Math.Max(0, startLine - searchLimit); i--)
        {
            var match = pattern.Match(lines[i]);
            if (match.Success)
            {
                int idx = int.Parse(match.Groups[1].Value);
                ulong value = Convert.ToUInt64(match.Groups[2].Value, 16);
                if (values[idx] == 0)
                {
                    toClear.Add(i);
                    values[idx] = value;
                }
                if (values.All(v => v != 0))
                    break;
            }
        }
        return values;
    }

    static ulong[] Extract_4x_uint(string[] lines, string varName, int startLine, int searchLimit = 1000)
    {
        uint[] values = new uint[4] { 0, 0, 0, 0 };
        var pattern = new Regex($@".*?{Regex.Escape(varName)}\.m128_.32\[(\d+)\]\s*=\s*0x([0-9A-Fa-f]+)");
        for (int i = startLine; i >= Math.Max(0, startLine - searchLimit); i--)
        {
            var match = pattern.Match(lines[i]);
            if (match.Success)
            {
                int idx = int.Parse(match.Groups[1].Value);
                uint value = Convert.ToUInt32(match.Groups[2].Value, 16);
                if (values[idx] == 0)
                {
                    toClear.Add(i);
                    values[idx] = value;
                }
                if (values.All(v => v != 0))
                    break;
            }
        }
        ulong[] ulongValues = new ulong[2];
        ulongValues[0] = (ulong)values.ElementAtOrDefault(0) | ((ulong)values.ElementAtOrDefault(1) << 32);
        ulongValues[1] = (ulong)values.ElementAtOrDefault(2) | ((ulong)values.ElementAtOrDefault(3) << 32);
        return ulongValues;
    }

    static ulong[] ExtractDwordValues(string[] lines, string varName, int startLine, int searchLimit = 2000)
    {
        List<uint> values = new List<uint>();

        string cleanvarname = varName;
        if (varName.IndexOf('[') != -1)
        {
            cleanvarname = varName.Remove(varName.IndexOf('['));
        }

        var pattern = new Regex($@".*?\*\(_DWORD\s*\*\)&?{Regex.Escape(cleanvarname)}(?:\[(0[xX][0-9a-fA-F]+|\d+)\])?\s*=\s*0x([0-9A-Fa-f]+)");

        uint startIndex = 0;
        if (varName.Contains("["))
        {
            var indexMatch = Regex.Match(varName, @"\[(0[xX][0-9a-fA-F]+|\d+)\]");
            if (indexMatch.Success)
            {
                startIndex = Convert.ToUInt32(indexMatch.Groups[1].Value, indexMatch.Groups[1].Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
            }
        }

        for (int i = startLine; i >= Math.Max(0, startLine - searchLimit); i--)
        {
            var match = pattern.Match(lines[i]);
            if (match.Success)
            {
                int idx = 0;
                try { idx = (int)Convert.ToUInt32(match.Groups[1].Value, match.Groups[1].Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10); } catch { }
                uint value = Convert.ToUInt32(match.Groups[2].Value, 16);
                while (values.Count <= idx)
                {
                    values.Add(0);
                }
                if (values[idx] == 0)
                {
                    values[idx] = value;

                    for (int x = 0; x < 4; x++)
                    {
                        if (idx == startIndex + x * 4)
                        {
                            toClear.Add(i);
                        }
                    }
                }
            }
        }

        ulong[] ulongValues = new ulong[2];
        ulongValues[0] = (ulong)values.ElementAtOrDefault((int)startIndex + 0 * 4) | ((ulong)values.ElementAtOrDefault((int)startIndex + 1 * 4) << 32); // Объединяем 0 и 1 индексы
        ulongValues[1] = (ulong)values.ElementAtOrDefault((int)startIndex + 2 * 4) | ((ulong)values.ElementAtOrDefault((int)startIndex + 3 * 4) << 32); // Объединяем 2 и 3 индексы

        return ulongValues;
    }


    public static byte[] StrBytesFromHex(string hex)
    {
        hex = hex.Replace("0x", "");
        hex = hex.Replace("-", "");
        List<byte> raw = new List<byte>();
        for (int i = 0; i < hex.Length / 2; i++)
        {
            byte c = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            if (c == 0)
                break;
            raw.Add(c);
        }
        return raw.ToArray();
    }
    public static byte[] BytesFromHex(string hex)
    {
        hex = hex.Replace("0x", "");
        hex = hex.Replace("-", "");
        List<byte> raw = new List<byte>();
        for (int i = 0; i < hex.Length / 2; i++)
        {
            byte c = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            raw.Add(c);
        }
        return raw.ToArray();
    }

    static string HexToString(string hexValue)
    {
        byte[] bytesValue = StrBytesFromHex(hexValue);

        return System.Text.Encoding.UTF8.GetString(bytesValue);
    }

    static string[] extractOps(string line)
    {
        var pattern = new Regex(@"(.*?)_mm_xor_ps\s*\(\s*(.*?)\s*,\s*(.*?).;");
        var match = pattern.Match(line);
        if (match.Success)
        {
            string zeroOp = match.Groups[1].Value.Trim();
            string firstOperand = match.Groups[2].Value.Trim();
            string secondOperand = match.Groups[3].Value.Trim();

            while (firstOperand.LastIndexOfAny(new char[] { '&', ')', '*', '&' }) > -1)
            {
                firstOperand = firstOperand.Substring(firstOperand.LastIndexOfAny(new char[] { '&', ')', '*', '&' }) + 1);
            }

            while (secondOperand.LastIndexOfAny(new char[] { '&', ')', '*', '&' }) > -1)
            {
                secondOperand = secondOperand.Substring(secondOperand.LastIndexOfAny(new char[] { '&', ')', '*', '&' }) + 1);
            }

            return new string[3] {zeroOp, firstOperand, secondOperand };
        }

        return new string[0];
    }
    static string FindSourceVariable(string[] lines, string varName, int startLine, int searchLimit = 50)
    {
        var pattern = new Regex($@"{Regex.Escape(varName)}\s*=\s*(v[\w\d_]+)\s*;");
        for (int i = startLine; i >= Math.Max(0, startLine - searchLimit); i--)
        {
            var match = pattern.Match(lines[i]);
            if (match.Success)
            {
                string res = FindSourceVariable(lines, match.Groups[1].Value, i, searchLimit);
                if (res != match.Groups[1].Value)
                    return FindSourceVariable(lines, match.Groups[1].Value, i, searchLimit);
                return match.Groups[1].Value;
            }
        }
        return varName; // Return the original name if no source variable is found.
    }

    // Update the process function to use the new source variable function
    static void ProcessCCode(string filename)
    {
        Console.WriteLine($"Load file: {filename}\n");
        string[] lines = File.ReadAllLines(filename);

        List<string> processedLines = new List<string>();
        processedLines.Add("");
        List<string> errorMessages = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("_mm_xor_ps"))
            {
                int findSource = 0;
            restart:
                Console.WriteLine($"Found xor: {lines[i].Trim()} \n");

                string[] parts = extractOps(lines[i]);
                if (parts.Length != 3)
                    continue;

                string op1 = parts[1];
                string op2 = parts[2];

                if (findSource != 0)
                {
                    // Find the source variable if necessary
                    if (findSource == 1)
                        op1 = FindSourceVariable(lines, op1, i);
                    else
                        op2 = FindSourceVariable(lines, op2, i);
                }

                ulong[] op1Values = Extract_2x_ulong(lines, op1, i);
                ulong[] op2Values = Extract_2x_ulong(lines, op2, i);

                if (op1Values.All(v => v == 0))
                {
                    op1Values = Extract_4x_uint(lines, op1, i);
                }

                if (op2Values.All(v => v == 0))
                {
                    op2Values = Extract_4x_uint(lines, op2, i);
                }

                if (op1Values.All(v => v == 0))
                {
                    op1Values = ExtractDwordValues(lines, op1, i);
                }

                if (op2Values.All(v => v == 0))
                {
                    op2Values = ExtractDwordValues(lines, op2, i);
                }

                if (op1Values.Any(v => v != 0))
                    Console.WriteLine($"Operand 1 values: {string.Join(", ", op1Values.Select(v => $"0x{v:X}"))} \n");

                if (op2Values.Any(v => v != 0))
                    Console.WriteLine($"Operand 2 values: {string.Join(", ", op2Values.Select(v => $"0x{v:X}"))} \n");

                if (op1Values.Any(v => v != 0) && op2Values.Any(v => v != 0))
                {
                    byte[] op1Bytes = BitConverter.GetBytes(op1Values[0]).Concat(BitConverter.GetBytes(op1Values[1])).ToArray();
                    byte[] op2Bytes = BitConverter.GetBytes(op2Values[0]).Concat(BitConverter.GetBytes(op2Values[1])).ToArray();

                    byte[] result = XorOperands(op1Bytes, op2Bytes);
                    string resultHex = BitConverter.ToString(result).Replace("-", "").ToUpper();
                    Console.WriteLine($"Start simulating _mm_xor_ps at line {i + 1}");
                    string hexString = HexToString(resultHex);


                    bool newline = resultHex.EndsWith("00") || string.IsNullOrEmpty(hexString);

                    if (!string.IsNullOrEmpty(hexString))
                    {
                        processedLines[processedLines.Count - 1] += hexString;
                    }

                    if (newline)
                        processedLines.Add(Environment.NewLine);

                    if (newline)
                    {
                        lines[i] = (parts[0].Length > 0 ? parts[0] : op1 + " =") + " (const char*)\"" + processedLines[processedLines.Count - 2].Replace("\n", "").Replace("\r", "") + "\"; // DECRYPTED";
                    }
                    else
                    {
                        toClear.Add(i);
                    }

                    Console.WriteLine($"Success");
                }
                else
                {
                    op1 = parts[1];
                    op2 = parts[2];

                    bool needrestart = false;
                    if (findSource == 0)
                        needrestart = true;

                    var missingOperands = new List<string>();
                    if (op1Values.All(v => v == 0))
                    {
                        findSource = 1;
                        missingOperands.Add(op1);
                    }
                    if (op2Values.All(v => v == 0))
                    {
                        findSource = 2;
                        missingOperands.Add(op2);
                    }

                    string errorMessage = $"Error at line {i + 1}: {lines[i].Trim()} - Missing operands: {string.Join(", ", missingOperands)}";

                    if (errorMessages.Count > 0)
                    {
                        if (errorMessages[errorMessages.Count - 1] != errorMessage)
                        {
                            errorMessages.Add(errorMessage);
                            Console.WriteLine(errorMessage);
                        }
                    }
                    else
                    {
                        errorMessages.Add(errorMessage);
                        Console.WriteLine(errorMessage);
                    }

                    if (needrestart)
                        goto restart;
                }
            }
        }

        // Reverse the processed lines to get the original order

        using (StreamWriter dumpFile = new StreamWriter("dump.txt", false, System.Text.Encoding.UTF8))
        {
            foreach (var line in processedLines)
            {
                dumpFile.Write(line);
            }
        }


        using (StreamWriter errorFile = new StreamWriter("error.txt", false, System.Text.Encoding.UTF8))
        {
            foreach (var error in errorMessages)
            {
                errorFile.WriteLine(error);
            }
        }

        Console.WriteLine($"Results written to dump.txt and errors written to error.txt");
        Console.WriteLine($"Fixed source saved to " + filename + ".fixed.txt");

        List<string> resLines = new List<string>();
        for(int i = 0; i < lines.Length; i++)
        {
            if (toClear.Contains(i))
                continue;
            resLines.Add(lines[i]);
        }

        string fileWithoutExtension = Path.GetFileNameWithoutExtension(filename);
        string extension = Path.GetExtension(filename);
        string newFilename = $"{fileWithoutExtension}_fixed{extension}";
        File.WriteAllLines(newFilename, resLines);
    }
    static void Main(string[] args)
    {
        try
        {
            File.Delete("error.txt");
        }
        catch { }
        try
        {
            File.Delete("extradump.txt");
        }
        catch { }
        try
        {
            File.Delete("dump.txt");
        }
        catch { }
        Console.WriteLine("Enter path to .c file saved by IDA HexRays with radix = 16:");
        string cFilename = Console.ReadLine().Replace("\"", "");

        try
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(cFilename));
        }catch{ }

        ProcessCCode(cFilename);
        List<byte[]> hexValues = ExtractHexValues(cFilename);
        List<string> xorResults = GenerateXorStrings(hexValues);
        SaveResultsToFile(xorResults, "extradump.txt");
    }
}
