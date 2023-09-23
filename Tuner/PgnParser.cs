using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Tuner
{
    internal class PgnParser
	{
		
		//parses PGN file to a list of FENs for every move in every game
		public static bool Parse(string pgnFile, string newFile)
		{
			Process pgnExtract = new Process();
			pgnExtract.StartInfo.FileName = "../../../pgn-extract.exe";
			pgnExtract.StartInfo.Arguments = $"-Wfen \"{pgnFile}\" --notags";
            pgnExtract.StartInfo.UseShellExecute = false;
            pgnExtract.StartInfo.RedirectStandardOutput = true;
            pgnExtract.StartInfo.RedirectStandardInput = true;
            try
			{
				pgnExtract.Start();
			} catch
			{
				Console.WriteLine("failed to start pgn-extract cli");
			}
            var stdout = pgnExtract.StandardOutput;

			StreamWriter outFile = new StreamWriter(newFile);

            var fenOutput = "";
            while ((fenOutput = stdout.ReadLine()) != null)
            {
				outFile.WriteLine(fenOutput);
            }
			outFile.Close();
            return true;
		}
	}
}
