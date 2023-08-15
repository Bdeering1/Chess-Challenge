// Written by Rhxydos
// Released under CC BY 4.0 License

using ChessChallenge.API;
using System;
using System.Diagnostics;

namespace ChessChallenge.Example{
    public class EvilBot : IChessBot
    {
        
        // Parameters to tweak how good it is.
        int movetime_ms = 200;
        double fish_ratio = 1;

        public Move Think(Board board, Timer timer)
        {
            //Test();
            var rand = new Random();
            if (rand.NextDouble() <= fish_ratio){
                Console.WriteLine("Querying Stockfish: FEN = " + board.GetFenString());
                string best_move_str = QueryStockfish(board);
                Console.WriteLine("Stockfish says best move is " + best_move_str);
                Move desired_move = new Move(best_move_str, board);
                return desired_move;
            }
            Move[] all_moves = board.GetLegalMoves();
            return all_moves[rand.Next(0,all_moves.Length)];
        }

        // This is just here for debugging purposes ; I might need it later, so it stays.
        public void Test(){
            string static_fen = "2r1kb2/1p3p2/p4q1p/4N3/3KP3/P1P1P3/NP5P/R1B2b2 b - - 2 27";
            Board test_board = Board.CreateBoardFromFEN(static_fen);
            string stockfish_output = QueryStockfish(test_board);
            Console.WriteLine("Starting Test");
            Console.WriteLine("Stockfish Output: " + stockfish_output);
            Move new_move = new Move(stockfish_output, test_board);
            bool is_valid = false;
            foreach(Move m in test_board.GetLegalMoves()){
                if (new_move.Equals(m)){
                    is_valid = true;
                }
            }

            Console.WriteLine("Is Valid Move?" + is_valid);
            return;
        }

        public string QueryStockfish(Board board)
        {
            Process cmd = new Process();
            // You will need to change this to 'cmd.exe' if you are on Windows!
            cmd.StartInfo.FileName = "cmd.exe";
            cmd.StartInfo.RedirectStandardInput = true;
            cmd.StartInfo.RedirectStandardOutput = true;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.StartInfo.UseShellExecute = false;
            cmd.Start();

            // Communicate With Stockfish
            cmd.StandardInput.WriteLine("stockfish");
            cmd.StandardInput.WriteLine("isready");
            string fen_string = board.GetFenString();
            cmd.StandardInput.WriteLine("position fen " + fen_string);
            cmd.StandardInput.WriteLine("go movetime " + movetime_ms.ToString());

            //There is probably a better way to figure out when it's done.
            System.Threading.Thread.Sleep(movetime_ms + 20);

            // Close Stockfish
            cmd.StandardInput.Flush();
            cmd.StandardInput.Close();
            cmd.WaitForExit();

            // Process Output
            string raw_output = cmd.StandardOutput.ReadToEnd();
            int move_index = raw_output.LastIndexOf("bestmove");
            string bestmove = raw_output.Substring(move_index + 9);
            int end_idx = bestmove.IndexOf(" ");
            if (end_idx < 0){
                return bestmove;
            }
            return bestmove.Substring(0,end_idx);
        }
    }
}