using ChessChallenge.API;
using System;
using System.Collections.Generic;

/*
 * Development Resources
 * -------------------------------------------------------------------------------------------------
 * Move Ordering:                                       https://rustic-chess.org/search/ordering/reason.html
 * Average moves per game:                              https://chess.stackexchange.com/a/4899
 * Effective branching factor:                          https://www.chessprogramming.org/Branching_Factor#EffectiveBranchingFactor
 * 
 * Tiny Chess League:                                   https://chess.stjo.dev/
 * Make EvilBot use Stockfish:                          https://github.com/SebLague/Chess-Challenge/discussions/311
 * Add buttons to play against different bots:          https://github.com/SebLague/Chess-Challenge/discussions/239
 * Chess Challenge Discord:                             https://discord.com/invite/pAadhun2px
 * 
 * Interesting looking bots:
 *  - https://github.com/nathanWolo/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs
 *  - https://github.com/dorinon/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs
 *  - https://github.com/Tjalle-S/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs 
 *  - https://github.com/Sidhant-Roymoulik/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs
 *  - https://github.com/outercloudstudio/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs
 *  - https://github.com/Nitish-Naineni/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs 
 */
public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private Dictionary<ulong, Move[]> moves_table = new();
    private int nodes;
    private int quiesce_nodes;
    private int tt_hits; //#DEBUG
    //private int timeForLastDepth;
    private int timeAllowed;

    private int search_depth = 1;
    private readonly int MAX_DEPTH = 15;
    
    private bool logged_side = false;
    private double moves = 0;

    private readonly int[] piece_val = { 0, 100, 317 /* 325 - 8 */, 325, 558 /* 550 + 8 */, 1000, 0 };
    private readonly int[] piece_phase = { 0, 0, 1, 1, 2, 4, 0 };
    private readonly int[] pawn_modifier = { 0, 0, 1, 0, -1, 0, 0 };

    record struct TTEntry(Move move, int score, int bound, int depth); // bound: 0 = exact, 1 = upper, 2 = lower
    private Dictionary<ulong, TTEntry> tt = new();


    public Move Think(Board b, Timer t)
    {
        if (!logged_side) { if (b.IsWhiteToMove && !logged_side) Console.WriteLine("Playing white"); else if (!logged_side) Console.WriteLine("Playing black"); logged_side = true; } //#DEBUG

        board = b;
        timer = t;
        nodes = quiesce_nodes = tt_hits = 0;
        //timeForLastDepth = 1;
        timeAllowed = GetTimeAllowance();

        Console.WriteLine(); //#DEBUG
        search_depth = 1;
        //int startTime = 0;
        while (search_depth <= MAX_DEPTH)
        {
            nodes = quiesce_nodes = tt_hits = 0;
            int score = NegaMax(0, -99999, 99999);
            if (timer.MillisecondsElapsedThisTurn > timeAllowed) break; //#DEBUG
            Console.WriteLine($"score: {score, -5} depth: {search_depth} nodes: {nodes,-6} quiesce nodes: {quiesce_nodes,-8} tt hits: {tt_hits, -5} delta: {timer.MillisecondsElapsedThisTurn/* - reg_delta*/}ms"); //#DEBUG
            search_depth++;

            //if the next iteration will take too much time, skip it
            //var timeForThisDepth = timer.MillisecondsElapsedThisTurn - startTime;
            //if (GetTimeForNextDepth(timeForThisDepth, timeForLastDepth) + timer.MillisecondsElapsedThisTurn > GetTimeAllowance()) { break; }
            //startTime = timer.MillisecondsElapsedThisTurn;
            //timeForLastDepth = Math.Max(timeForThisDepth, 1);
        }

        //Console.WriteLine($"{$"{timer.MillisecondsElapsedThisTurn:0.##}ms", -8} avg: {$"{(timer.GameStartTimeMilliseconds - timer.MillisecondsRemaining) / ++moves:0}ms", -8} depth: {search_depth}"); //#DEBUG

        return GetOrderedLegalMoves()[0];
    }


    /* SEARCH ---------------------------------------------------------------------------------- */
    private int NegaMax(int depth, int alpha, int beta)
    {
        if (timer.MillisecondsElapsedThisTurn > timeAllowed) return -100000;
        

        nodes++;

        if (tt.TryGetValue(board.ZobristKey, out var entry) && entry.depth >= search_depth - depth)
        {
            tt_hits++; //#DEBUG
            if (entry.bound == 0) return entry.score; // exact score
            if (entry.bound == 1 && entry.score <= alpha) return alpha; // fail low
            if (entry.bound == 2 && entry.score >= beta) return beta; // fail high
        }
        if (depth >= search_depth) return Quiesce(alpha, beta);

        Move? pv = null;
        foreach (var move in GetOrderedLegalMoves())
        {
            board.MakeMove(move);
            int score = -NegaMax(depth + 1, -beta, -alpha);
            board.UndoMove(move);

            if (score >= beta)
            {
                tt[board.ZobristKey] = new(move, beta, search_depth - depth, 2); // score is at LEAST beta (lower bound)
                return beta;
            }
            if (score > alpha) { alpha = score; pv = move; continue; }
            tt[board.ZobristKey] = new(move, alpha, search_depth - depth, 1); // score is at MOST alpha (upper bound)
        }

        if (pv.HasValue)
        {
            SetPV(pv.Value, depth); // depth is temporary
            tt[board.ZobristKey] = new(pv.Value, alpha, search_depth - depth, 0); // score is EXACTLY alpha (exact bound)
        }
        return alpha;
    }

    private int Quiesce(int alpha, int beta)
    {
        quiesce_nodes++;
        int stand_pat = Eval();
        if (stand_pat >= beta) return beta;
        if (stand_pat > alpha) alpha = stand_pat;

        foreach (var move in board.GetLegalMoves(true))
        {
            board.MakeMove(move);
            int score = -Quiesce(-beta, -alpha);
            board.UndoMove(move);

            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }

        return alpha;
    }


    /* MOVE ORDERING --------------------------------------------------------------------------- */
    private void SetPV(Move pv_move, int depth)
    {
        var moves = GetOrderedLegalMoves();
        var pv_idx = 0;
        while (moves[pv_idx] != pv_move) pv_idx++;

        if (pv_idx == moves.Length) //#DEBUG
        { //#DEBUG
            Console.WriteLine("About to crash (couldn't find pv move)"); //#DEBUG
            Console.WriteLine(board.CreateDiagram()); //#DEBUG
            Console.WriteLine($"\ndepth: {depth}\ntt hit: {tt.ContainsKey(board.ZobristKey)}\npv move: {pv_move}\nmove list ({moves.Length} moves): "); //#DEBUG
            foreach (var move in moves) Console.Write($"{move}, "); //#DEBUG
            Console.WriteLine(); //#DEBUG
        } //#DEBUG

        while (pv_idx > 0) moves[pv_idx] = moves[--pv_idx];
        moves[0] = pv_move;
    }

    private Move[] GetOrderedLegalMoves()
    {
        if (moves_table.TryGetValue(board.ZobristKey, out var moves)) return moves;

        moves = board.GetLegalMoves();
        for (var i = 1; i < moves.Length;)
        {
            //store the original element for inserting later
            var move = moves[i];
            int j = i++ - 1;

            //go down the array, swapping until we reach a spot where we can insert
            while (j >= 0 && GetPrecedence(moves[j]) > GetPrecedence(move)) moves[j + 1] = moves[j--];
            moves[j + 1] = move; //insert move
        }

        moves_table[board.ZobristKey] = moves;
        return moves;
    }

    private int GetPrecedence(Move move) //gets precedence of a move for move ordering {promotions, castles, captures, everything else}
        //queen promotions: 0
        //castles: 1
        //captures: 6-14
        //everything else: 20
        => 
            (move.IsPromotion && move.PromotionPieceType == PieceType.Queen) ? 0 
            : move.IsCapture ? 10 - (int)move.CapturePieceType + (int)move.MovePieceType 
            : move.IsCastles ? 1 
            : 20;


    /* TIME MANAGEMENT ------------------------------------------------------------------------- */
    //private int GetTimeForNextDepth(int timeForThisDepth, int timeForLastDepth)
    //    => (timeForThisDepth / timeForLastDepth) * timeForThisDepth;

    private int GetTimeAllowance()
    {
        var plyCount = board.PlyCount / 2; //since we want to input full moves to the function
        //(based on this curve: https://www.desmos.com/calculator/gee60oepkk)
        return timer.MillisecondsRemaining / ((int)(59.3 + (72830 - 2330 * plyCount) / (2644 + plyCount * (10 + plyCount))) / 2/*since this calculation is in # of half moves*/);
    }


    /* EVALUATION ------------------------------------------------------------------------------ */
    /* 
     * Eval Options
     * --------------------------------------------------------------------------------------------------
     * Piece Square                                 https://www.chessprogramming.org/Piece-Square_Tables
     *   - could be programatically generated
     * Piece Specific Eval                          https://www.chessprogramming.org/Evaluation_of_Pieces
     * Pattern Evaluation                           https://www.chessprogramming.org/Evaluation_Patterns
     * Mobility                                     https://www.chessprogramming.org/Mobility
     * Center Control                               https://www.chessprogramming.org/Center_Control
     * Connectivity                                 https://www.chessprogramming.org/Connectivity
     * King Safety                                  https://www.chessprogramming.org/King_Safety
     * Space                                        https://www.chessprogramming.org/Space
     * Tempo                                        https://www.chessprogramming.org/Tempo
     */
    private int Eval()
    {
        if (board.IsDraw()) return 0;
        if (board.IsInCheckmate()) return -50000;

        int score = 0,
            side_multiplier = board.IsWhiteToMove ? 1 : -1,
            pawns_count = 16 - board.GetAllPieceLists()[0].Count - board.GetAllPieceLists()[6].Count;

        //int mg = 0, eg = 0, phase = 0;
        foreach (bool is_white in new[] { true, false }) //true = white, false = black
        {
            for (var piece_type = PieceType.Pawn; piece_type++ < PieceType.King;)
            {
                int piece = (int)piece_type;//, idx;
                ulong mask = board.GetPieceBitboard(piece_type, is_white);
                while (mask != 0)
                {
                    int lsb = BitboardHelper.ClearAndGetIndexOfLSB(ref mask);
                    //phase += piece_phase[piece];
                    //idx = 128 * (piece - 1) + BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ (side_to_move ? 56 : 0);
                    score /*mg*/ += piece_val[piece] + pawn_modifier[piece] * pawns_count;// + GetPstVal(idx);
                    //eg += piece_val[piece] + pawn_modifier[piece] * pawns_count;// + GetPstVal(idx + 64);
                }
            }

            score = -score;
            //mg = -mg;
            //eg = -eg;
        }

        //score = (mg * phase + eg * (24 - phase)) / 24; // max phase = 24

        /* Mobility Score */
        foreach (var move in board.GetLegalMoves()) if (move.MovePieceType != PieceType.Queen) score += side_multiplier;
        board.ForceSkipTurn();
        foreach (var move in board.GetLegalMoves()) if (move.MovePieceType != PieceType.Queen) score += -side_multiplier;
        board.UndoSkipTurn();

        return score * side_multiplier;
    }

    private int GetPstVal(int idx)
        => 0;
}