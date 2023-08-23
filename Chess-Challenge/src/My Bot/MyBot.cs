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
    private int nodes;
    private int quiesce_nodes;
    private int tt_hits; //#DEBUG
    private int timeAllowed;

    private int search_depth = 1;
    private readonly int MAX_DEPTH = 15;
    
    private bool logged_side = false;
    private double moves = 0;

    private readonly int[] piece_val = { 0, 100, 317 /* 325 - 8 */, 325, 558 /* 550 + 8 */, 1000, 0 };
    private readonly int[] piece_phase = { 0, 0, 1, 1, 2, 4, 0 };
    private readonly int[] pawn_modifier = { 0, 0, 1, 0, -1, 0, 0 };

    private Dictionary<ulong, Move[]> moves_table = new();
    //record struct TTEntry(Move move, int score, int bound, int depth); // bound: 0 = exact, 1 = upper, 2 = lower
    private Dictionary<ulong, (int, int, int)> tt = new(); // (score, bound, depth), bound -> 0 = exact, 1 = upper, 2 = lower

    private ulong[] packedPsts = PstPacker.Generate();

    public Move Think(Board _board, Timer _timer)
    {
        if (!logged_side) { if (_board.IsWhiteToMove && !logged_side) Console.WriteLine("Playing white"); else if (!logged_side) Console.WriteLine("Playing black"); logged_side = true; } //#DEBUG

        board = _board;
        timer = _timer;
        nodes = quiesce_nodes = tt_hits = 0;
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

        // var move = GetOrderedLegalMoves()[0]; //#DEBUG
        // Console.WriteLine($"Move: {move} PST Val (Move.To): {GetPstVal(move.TargetSquare.Index, (int)move.MovePieceType - 1, board.IsWhiteToMove, true)}"); //#DEBUG

        return GetOrderedLegalMoves()[0];
    }


    /* SEARCH ---------------------------------------------------------------------------------- */
    private int NegaMax(int depth, int alpha, int beta)
    {
        if (timer.MillisecondsElapsedThisTurn > timeAllowed) return 11111;
        
        nodes++;

        if (tt.TryGetValue(board.ZobristKey, out var entry) && entry.Item3 >= search_depth - depth) // Item1 -> score, Item2 -> bouond, Item3 -> depth
        {
            tt_hits++; //#DEBUG
            if (entry.Item2 == 0) return entry.Item1; // exact score
            if (entry.Item2 == 1 && entry.Item1 <= alpha) return alpha; // fail low
            if (entry.Item2 == 2 && entry.Item1 >= beta) return beta; // fail high
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
                tt[board.ZobristKey] = (beta, search_depth - depth, 2); // score is at LEAST beta (lower bound)
                return beta;
            }
            if (score > alpha) { alpha = score; pv = move; continue; }
            tt[board.ZobristKey] = (alpha, search_depth - depth, 1); // score is at MOST alpha (upper bound)
        }

        if (pv.HasValue)
        {
            SetPV(pv.Value, depth); // depth is temporary
            tt[board.ZobristKey] = (alpha, search_depth - depth, 0); // score is EXACTLY alpha (exact bound)
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
    private int GetTimeAllowance()
    {
        var move_count = board.PlyCount / 2; //since we want to input full moves to the function
        //(based on this curve: https://www.desmos.com/calculator/gee60oepkk)
        return timer.MillisecondsRemaining / ((60 + (72830 - 2330 * move_count) / (2644 + move_count * (10 + move_count))) / 2 /*since this calculation is in # of half moves*/);
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

        int mg = 0, eg = 0, phase = 0;
        foreach (bool is_white in new[] { true, false }) //true = white, false = black
        {
            for (var piece_type = PieceType.None; piece_type++ < PieceType.King;)
            {
                int piece = (int)piece_type;//, idx;
                ulong mask = board.GetPieceBitboard(piece_type, is_white);
                while (mask != 0)
                {
                    int lsb = BitboardHelper.ClearAndGetIndexOfLSB(ref mask);
                    phase += piece_phase[piece];

                    mg += piece_val[piece] + pawn_modifier[piece] * pawns_count + GetPstVal(lsb, piece - 1, is_white, 0);
                    eg += piece_val[piece] + pawn_modifier[piece] * pawns_count + GetPstVal(lsb, piece - 1, is_white, 6);
                }
            }

            mg = -mg;
            eg = -eg;
        }

        mg += 10 * side_multiplier;
        score = (mg * phase + eg * (24 - phase)) / 24; // max phase = 24

        /* Mobility Score */
        //foreach (var move in board.GetLegalMoves()) if (move.MovePieceType != PieceType.Queen) score += side_multiplier;
        //score += GetOrderedLegalMoves().Length;
        board.ForceSkipTurn();
        //foreach (var move in board.GetLegalMoves()) if (move.MovePieceType != PieceType.Queen) score -= side_multiplier;
        //score -= GetOrderedLegalMoves().Length;
        board.UndoSkipTurn();

        return score * side_multiplier;
    }

    private int GetPstVal(int lsb, int type, bool is_white, int table_shift, bool debug = false) // table_shift - should be either 0 (mg PSTs) or 6 (eg PSTs)
    {
        var file = lsb % 8;
        var rank = lsb / 8;
        if (debug) Console.WriteLine($"type: {type + 1} lsb: {lsb}, rank: {rank + 1}, file: {file + 1}, pst: {type * 4 + (file)}, pst_val: {(sbyte)((packedPsts[type * 4 + (file > 3 ? 7 - file : file)] >> (is_white ? 7 - rank : rank) * 8) & 0xFF)}"); //#DEBUG
        return (sbyte)((packedPsts[type * 4 + (file > 3 ? 7 - file : file) + table_shift] >> (is_white ? 7 - rank : rank) * 8) & 0xFF);
    }
}