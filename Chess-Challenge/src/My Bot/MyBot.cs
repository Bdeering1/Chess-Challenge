﻿using ChessChallenge.API;
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
    private int tt_hits;

    private int search_depth = 1;
    private readonly int MAX_DEPTH = 15;

    private bool logged_side = false;
    private double moves = 0;

    private readonly int[] piece_val = { 0, 100, 317 /* 325 - 8 */, 325, 558 /* 550 + 8 */, 1000, 0 };
    private readonly int[] piece_phase = { 0, 0, 1, 1, 2, 4, 0 };
    private readonly int[] pawn_modifier = { 0, 0, 1, 0, -1, 0, 0 };

    struct TTEntry
    {
        public Move move;
        public int score, depth, bound; // bound: 0 = exact, 1 = lower, 2 = upper

        public TTEntry(Move _move, int _score, int _depth, int _bound)
        {
            move = _move; score = _score; depth = _depth; bound = _bound;
        }
    }

    private Dictionary<ulong, TTEntry> tt = new();


    public Move Think(Board board, Timer timer)
    {
        if (!logged_side)
        {
            if (board.IsWhiteToMove && !logged_side) Console.WriteLine("Playing white");
            else if (!logged_side) Console.WriteLine("Playing black");
            logged_side = true;
        }

        this.board = board;
        this.timer = timer;
        nodes = quiesce_nodes = tt_hits = 0;

        //Console.WriteLine("\nRegular search:");
        //search_depth = MAX_DEPTH;
        //moves_table = new();
        //int score = NegaMax(0, -99999, 99999);
        //var reg_delta = timer.MillisecondsElapsedThisTurn;
        //Console.WriteLine($"score: {score, -5} depth: {search_depth} nodes: {nodes,-6} quiesce nodes: {quiesce_nodes,-8} delta: {reg_delta}ms");

        //Console.WriteLine("\nIterative deepening:");
        search_depth = 1;
        //moves_table = new();
        int startTime = 0;
        while (search_depth <= MAX_DEPTH)
        {
            nodes = quiesce_nodes = tt_hits = 0;
            int score = NegaMax(0, -99999, 99999);
            //Console.WriteLine($"score: {score, -5} depth: {search_depth} nodes: {nodes,-6} quiesce nodes: {quiesce_nodes,-8} tt hits: {tt_hits, -5} delta: {timer.MillisecondsElapsedThisTurn/* - reg_delta*/}ms");
            search_depth++;

            //if the next iteration will take too much time, skip it
            if (GetTimeForNextDepth((double)(timer.MillisecondsElapsedThisTurn - startTime) / (nodes + quiesce_nodes)) / 2 + timer.MillisecondsElapsedThisTurn > GetTimeAllowance()) break;
            startTime = timer.MillisecondsElapsedThisTurn;
        }

        //Console.WriteLine($"{$"{timer.MillisecondsElapsedThisTurn:0.##}ms", -8} avg: {$"{(timer.GameStartTimeMilliseconds - timer.MillisecondsRemaining) / ++moves:0.##}ms", -10} depth: {search_depth}");
        
        return GetOrderedLegalMoves()[0];
    }


    /* SEARCH ---------------------------------------------------------------------------------- */
    private int NegaMax(int depth, int alpha, int beta)
    {
        nodes++;

        if (tt.TryGetValue(board.ZobristKey, out var entry) && entry.depth >= search_depth - depth && entry.bound == 0)
        {
            tt_hits++;
            return entry.score; // prefer evals from lower depth (and exact bound for now)
        }
        if (depth >= search_depth) return Quiesce(alpha, beta);

        Move? pv = null;
        foreach (var move in GetOrderedLegalMoves())
        {
            board.MakeMove(move);
            int score = -NegaMax(depth + 1, -beta, -alpha);
            board.UndoMove(move);

            if (score >= beta) return beta;
            if (score > alpha) { alpha = score; pv = move; }
        }

        if (pv.HasValue)
        {
            SetPV(pv.Value, depth); // depth is temporary
            tt[board.ZobristKey] = new(pv.Value, alpha, search_depth - depth, 0);
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
        foreach (var m in moves)
        {
            if (m == pv_move) break;
            pv_idx++;
        }

        if (pv_idx == moves.Length)
        {
            Console.WriteLine("About to crash (couldn't find pv move)");
            Console.WriteLine(board.CreateDiagram());
            Console.WriteLine($"\ndepth: {depth}\ntt hit: {tt.ContainsKey(board.ZobristKey)}\npv move: {pv_move}\nmove list ({moves.Length} moves): ");
            foreach (var move in moves)
            {
                Console.Write($"{move}, ");
            }
            Console.WriteLine();
        }

        var i = 0;
        while (i < pv_idx)
        {
            moves[i + 1] = moves[i];
            i++;
        }
        moves[0] = pv_move;
    }

    private Move[] GetOrderedLegalMoves()
    {
        if (moves_table.TryGetValue(board.ZobristKey, out var moves)) return moves;

        moves = board.GetLegalMoves();
        for (var i = 1; i < moves.Length; i++)
        {
            //store the original element for inserting later
            var move = moves[i];
            int j = i - 1;

            //go down the array, swapping until we reach a spot where we can insert
            while (j >= 0 && GetPrecedence(moves[j]) > GetPrecedence(move))
            {
                moves[j + 1] = moves[j--];
            }
            moves[j + 1] = move; //insert move
        }

        moves_table[board.ZobristKey] = moves;
        return moves;
    }

    private int GetPrecedence(Move move) //gets precedence of a move for move ordering {promotions, castles, captures, everything else}
    {
        //queen promotions: 0
        //castles: 1
        //captures: 6-14
        //everything else: 20
        return 
            (move.IsPromotion && move.PromotionPieceType == PieceType.Queen) ? 0 
            : move.IsCapture ? 10 - (int)move.CapturePieceType + (int)move.MovePieceType 
            : move.IsCastles ? 1 
            : 20;
    }


    /* TIME MANAGEMENT ------------------------------------------------------------------------- */
    private int GetTimeForNextDepth(double timePerNode)
    {
        return (int)(Math.Pow(nodes + quiesce_nodes, 1.3) * timePerNode);
    }

    private int GetTimeAllowance() //TODO: make this change based on opponent time left
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
            for (var piece_type = PieceType.Pawn; piece_type < PieceType.King; piece_type++)
            {
                int piece = (int)piece_type;//, idx;
                ulong mask = board.GetPieceBitboard(piece_type, is_white);
                while (mask != 0)
                {
                    int lsb = BitboardHelper.ClearAndGetIndexOfLSB(ref mask);
                    if (piece == 5) score -= BitboardHelper.GetNumberOfSetBits(BitboardHelper.GetSliderAttacks(PieceType.Queen, new Square(lsb), board));
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
        score += side_multiplier * GetOrderedLegalMoves().Length;
        board.ForceSkipTurn();
        score += -side_multiplier * GetOrderedLegalMoves().Length;
        board.UndoSkipTurn();

        return score * side_multiplier;
    }

    private int GetPstVal(int idx)
    {
        return 0;
    }
}