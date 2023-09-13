using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tuner
{
    internal class CuteChess
    {

        Process cutechess;

        public CuteChess()
        {
            cutechess = new Process();
            cutechess.StartInfo.UseShellExecute = false;
            cutechess.StartInfo.RedirectStandardOutput = true;
            cutechess.StartInfo.RedirectStandardInput = true;
            string file = Path.Combine("../../../../", "cutechess", "cutechess-cli.exe");
            cutechess.StartInfo.FileName = file;
        }

        public string Test(string engine1, string engine2, int rounds, int threads)
        {
            cutechess.StartInfo.Arguments = $"-engine conf={engine1} -engine conf={engine2} -each tc=0/60+0 -maxmoves 1000 -games 2 -repeat -recover -resultformat wide2 -ratinginterval 10 -rounds {rounds} -concurrency {threads} -tournament gauntlet -pgnout out.pgn -openings file=\"{Path.GetFullPath(Path.Combine("../../../../", "Tuner", "UHO_XXL_+1.00_+1.29.epd"))}\" format=epd order=random";

            try
            {
                cutechess.Start();
            } catch (System.ComponentModel.Win32Exception)
            {
                Console.WriteLine("failed to find cutechess executable");
            }

            //var inp = cutechess.StandardInput;
            //inp.WriteLine("-engine conf=stockfish ^\r\n-engine conf=engine1 ^\r\n-each tc=0/60+0 ^\r\n-maxmoves 1000 ^\r\n-games 2 ^\r\n-repeat ^\r\n-resultformat wide2 ^\r\n-ratinginterval 10 ^\r\n-rounds 100 ^\r\n-concurrency 4 ^\r\n-tournament gauntlet ^\r\n-pgnout out.pgn ^\r\n-openings file=C:\\Users\\Josh\\Desktop\\chess\\opening_books\\UHO_XXL_2022_+100_+129.epd format=epd");
            //inp.Write("cutechess-cli.exe ^");
            //inp.WriteLine($"-engine conf={engine1} ^");
            //inp.WriteLine($"-engine conf={engine2} ^"); 
            //inp.WriteLine("-each tc=0/60+0 ^");
            //inp.WriteLine("-maxmoves 1000 ^");
            //inp.WriteLine("-games 2 ^");
            //inp.WriteLine("-repeat ^");
            //inp.WriteLine("-resultformat wide2 ^");
            //inp.WriteLine("-ratinginterval 10 ^");
            //inp.WriteLine("-concurrency 4 ^");
            //inp.WriteLine("-tournament gauntlet ^");
            //inp.WriteLine("-pgnout out.pgn ^");
            //inp.WriteLine("-openings file=C:\\Users\\Josh\\Desktop\\chess\\opening_books\\UHO_XXL_2022_+100_+129.epd format=epd ");
            
            var line = cutechess.StandardOutput.ReadLine();
            while (line != "Finished match") {
                if (line != null) Console.WriteLine(line);
                line = cutechess.StandardOutput.ReadLine();
            }

            return "done";
        }

    }
}