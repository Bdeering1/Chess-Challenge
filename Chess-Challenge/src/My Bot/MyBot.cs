using ChessChallenge.API;
using System;
using System.Collections.Generic;

/*
 * Development Resources
 * -------------------------------------------------------------------------------------------------
 * Move Ordering:                                       https://rustic-chess.org/search/ordering/reason.html
 * Average moves per game:                              https://chess.stackexchange.com/a/4899
 * Effective branching factor:                          https://www.chessprogramming.org/Branching_Factor#EffectiveBranchingFactor
 * History Heuristic                                    https://www.chessprogramming.org/History_Heuristic
 * Killer Moves                                         https://www.chessprogramming.org/Killer_Heuristic
 * 
 * Tiny Chess League:                                   https://chess.stjo.dev/
 * Make EvilBot use Stockfish:                          https://github.com/SebLague/Chess-Challenge/discussions/311
 * Add buttons to play against different bots:          https://github.com/SebLague/Chess-Challenge/discussions/239
 * Chess Challenge Discord:                             https://discord.com/invite/pAadhun2px
 * 
 * Interesting looking bots:
 *  - https://github.com/nathanWolo/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs
 *  - https://github.com/Tjalle-S/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs 
 *  - https://github.com/Sidhant-Roymoulik/Chess-Challenge/blob/main/Chess-Challenge/src/My%20Bot/MyBot.cs
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
    
    private bool logged_side = false; //#DEBUG


    private readonly int[] piece_val = { 0, 100, 317 /* 325 - 8 */, 333 /* 325 + 8 */, 558 /* 550 + 8 */, 1000, 0 };
    private readonly int[] piece_phase = { 0, 0, 1, 1, 2, 4, 0 };
    private readonly int[] pawn_modifier = { 0, 0, 1, -1, -1, 0, 0 };

    private Dictionary<ulong, Move[]> moves_table = new();
    //record struct TTEntry(Move move, int score, int bound, int depth); // bound: 0 = exact, 1 = upper, 2 = lower
    private Dictionary<ulong, (Move, int, int, int)> tt = new(); // (move, score, bound, depth), bound -> 0 = exact, 1 = upper, 2 = lower

    private ulong[] packed_psts = PstPacker.Generate();

    public Move Think(Board _board, Timer _timer)
    {
        if (!logged_side) { if (_board.IsWhiteToMove && !logged_side) Console.WriteLine("Playing white"); else if (!logged_side) Console.WriteLine("Playing black"); logged_side = true; } //#DEBUG

        board = _board;
        timer = _timer;
        nodes = quiesce_nodes = tt_hits = 0; //#DEBUG
        timeAllowed = 2 * timer.MillisecondsRemaining / (60 + 1444 / (board.PlyCount / 2 /* <- # of full moves */ + 27));

        Console.WriteLine(); //#DEBUG
        search_depth = 1;
        while (search_depth <= MAX_DEPTH)
        {
            nodes = quiesce_nodes = tt_hits = 0;
            int score = NegaMax(0, -99999, 99999);
            Console.WriteLine($"PV {GetPV()} score: {score, -5} depth: {search_depth} nodes: {nodes,-6} quiesce nodes: {quiesce_nodes,-8} tt hits: {tt_hits, -5} delta: {timer.MillisecondsElapsedThisTurn/* - reg_delta*/}ms"); //#DEBUG
            search_depth++;

            if (timer.MillisecondsElapsedThisTurn > timeAllowed) break;
        }

        //Console.WriteLine($"{$"{timer.MillisecondsElapsedThisTurn:0.##}ms", -8} avg: {$"{(timer.GameStartTimeMilliseconds - timer.MillisecondsRemaining) / ++moves:0}ms", -8} depth: {search_depth}"); //#DEBUG

        // Console.WriteLine($"Move: {GetPV()} PST Val (Move.To): {GetPstVal(GetPV().TargetSquare.Index, (int)GetPV().MovePieceType - 1, board.IsWhiteToMove, true)}"); //#DEBUG

        Console.WriteLine(); //#DEBUG
        var pv_moves = new Stack<Move>(); //#DEBUG
        for (int i = 0; i < search_depth; i++) { //#DEBUG
            Console.WriteLine($"PV {GetPV()} static score: {Evaluation.Debug(board, packed_psts, piece_val, piece_phase, pawn_modifier), -5} depth: {i}"); //#DEBUG
            pv_moves.Push(GetPV()); //#DEBUG
            board.MakeMove(GetPV()); //#DEBUG
        }
        for (int i = 0; i < search_depth; i++) { //#DEBUG
            board.UndoMove(pv_moves.Pop()); //#DEBUG
        } //#DEBUG

        //Move best_move = default;
        //if (tt.TryGetValue(board.ZobristKey, out var entry)) best_move = entry.Item1;

        return tt[board.ZobristKey].Item1;
    }


    /* SEARCH ---------------------------------------------------------------------------------- */
    private int NegaMax(int depth, int alpha, int beta)
    {   
        nodes++; //#DEBUG

        /* Get Transposition Values */
        if (tt.TryGetValue(board.ZobristKey, out var entry) && entry.Item4 >= search_depth - depth) // Item1 -> score, Item2 -> bouond, Item3 -> depth
        {
            tt_hits++; //#DEBUG
            if (entry.Item3 == 0) return entry.Item2; // exact score
            if (entry.Item3 == 1 && entry.Item2 <= alpha) return alpha; // fail low
            if (entry.Item3 == 2 && entry.Item2 >= beta) return beta; // fail high
        }

        /* Quiescence Search (delta pruning) */
        var q_search = depth >= search_depth;
        int score;
        if (q_search) {
            score = Eval();
            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }

        Move pv = default;

        var moves = board.GetLegalMoves();
        for (var i = 1; i < moves.Length;)
        {
            //store the original element for inserting later
            var move = moves[i];
            int j = i++ - 1;
            
            if (tt.TryGetValue(board.ZobristKey, out var tt_entry)) pv = tt_entry.Item1;
            //go down the array, swapping until we reach a spot where we can insert
            while (j >= 0 && GetPrecedence(moves[j], pv) > GetPrecedence(move, pv)) moves[j + 1] = moves[j--];
            moves[j + 1] = move; //insert move
        }

        /* Main Search */
        foreach (var move in q_search ? board.GetLegalMoves(true) : moves)
        {
            board.MakeMove(move);
            score = -NegaMax(depth + 1, -beta, -alpha);
            board.UndoMove(move);

            if (timer.MillisecondsElapsedThisTurn > timeAllowed) return 11111;

            if (score > alpha) { alpha = score; pv = move; }
            if (score >= beta) break;
        }

        /* Set Transposition Values */
        var best = Math.Min(alpha, beta);
        if (!q_search)
            tt[board.ZobristKey] = (
                pv,
                best,
                search_depth - depth,
                alpha >= beta ? 2 /* lower bound */
                : pv != default(Move) ? 0 /* exact bound */
                : 1 /* upper bound */
            );
        return best;
    }


    /* MOVE ORDERING --------------------------------------------------------------------------- */
    private int GetPrecedence(Move move, Move pv_move) //gets precedence of a move for move ordering {promotions, castles, captures, everything else}
        //pv move: 0
        //queen promotions: 1
        //castles: 2
        //captures: 6-14
        //everything else: 20
        => 
            move == pv_move ? 0
            : (move.IsPromotion && move.PromotionPieceType == PieceType.Queen) ? 1 
            : move.IsCapture ? 10 - (int)move.CapturePieceType + (int)move.MovePieceType 
            : move.IsCastles ? 2
            : 20;


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
        //score += board.GetLegalMoves().Length;
        //board.ForceSkipTurn();
        //foreach (var move in board.GetLegalMoves()) if (move.MovePieceType != PieceType.Queen) score -= side_multiplier;
        //score -= board.GetLegalMoves().Length;
        //board.UndoSkipTurn();

        return score * side_multiplier;
    }

    private int GetPstVal(int lsb, int type, bool is_white, int table_shift, bool debug = false) // table_shift - should be either 0 (mg PSTs) or 6 (eg PSTs)
    {
        var file = lsb % 8;
        var rank = lsb / 8;
        if (debug) Console.WriteLine($"type: {type + 1} lsb: {lsb}, rank: {rank + 1}, file: {file + 1}, pst: {type * 4 + (file)}, pst_val: {(sbyte)((packed_psts[type * 4 + (file > 3 ? 7 - file : file)] >> (is_white ? 7 - rank : rank) * 8) & 0xFF)}"); //#DEBUG
        return (sbyte)((packed_psts[type * 4 + (file > 3 ? 7 - file : file) + table_shift] >> (is_white ? 7 - rank : rank) * 8) & 0xFF);
    }

    private Move GetPV() { //#DEBUG
        Move pv = default; //#DEBUG
        if (tt.TryGetValue(board.ZobristKey, out var entry)) pv = entry.Item1; //#DEBUG
        return pv; //#DEBUG
    }
}