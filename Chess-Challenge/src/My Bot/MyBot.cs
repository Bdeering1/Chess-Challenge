using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

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
 * 
 * Notes
 * -------------------------------------------------------------------------------------------------
 * - look into issue with making illegal moves ** this should be fixed
 * - look into issue with drawing in winning positions
 * - refactor transposition pruning code
 * - add history heuristic
 * - possibly refactor packed PSTs to use decimals
 * - add non-stalemate draw detection in NegaMax
 */
public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private int nodes; //#DEBUG
    private int quiesce_nodes; //#DEBUG
    private int tt_hits; //#DEBUG
    private int timeAllowed;

    private int search_depth = 1;
    private readonly int MAX_DEPTH = 15;

    private Move root_pv;
    
    private bool logged_side = false; //#DEBUG

    private ulong root_key; //#DEBUG


    private readonly int[] piece_val = { 0, 100, 317 /* 325 - 8 */, 333 /* 325 + 8 */, 558 /* 550 + 8 */, 1000, 0 };
    private readonly int[] piece_phase = { 0, 0, 1, 1, 2, 4, 0 };
    private readonly int[] pawn_modifier = { 0, 0, 1, -1, -1, 0, 0 };

    private Dictionary<ulong, Move[]> moves_table = new();
    private Dictionary<ulong, (Move, int, int, int)> tt = new(); // (move, score, bound, depth_left), bound -> 0 = exact, 1 = upper, 2 = lower

    private ulong[] packed_psts = PstPacker.Generate();

    public Move Think(Board _board, Timer _timer)
    {
        if (!logged_side) { if (_board.IsWhiteToMove && !logged_side) Console.WriteLine("Playing white"); else if (!logged_side) Console.WriteLine("Playing black"); logged_side = true; } //#DEBUG

        board = _board;
        timer = _timer;
        nodes = quiesce_nodes = tt_hits = 0; //#DEBUG
        timeAllowed = 2 * timer.MillisecondsRemaining / (60 + 1444 / (board.PlyCount / 2 /* <- # of full moves */ + 27));

        root_key = board.ZobristKey; //#DEBUG

        //Console.WriteLine(); //#DEBUG
        search_depth = 1;
        while (search_depth <= MAX_DEPTH)
        {
            nodes = tt_hits = 0; //#DEBUG
            int score = NegaMax(0, -99999, 99999);
            //Console.WriteLine($"PV {GetPV()} score: {score, -5} depth: {search_depth} nodes: {nodes,-6} quiesce nodes: {quiesce_nodes,-8} tt hits: {tt_hits, -5} delta: {timer.MillisecondsElapsedThisTurn/* - reg_delta*/}ms"); //#DEBUG
            search_depth++;

            if (timer.MillisecondsElapsedThisTurn > timeAllowed) break;
        }

        /* Timing Debug */
        //Console.WriteLine($"{$"{timer.MillisecondsElapsedThisTurn:0.##}ms", -8} avg: {$"{(timer.GameStartTimeMilliseconds - timer.MillisecondsRemaining) / ++moves:0}ms", -8} depth: {search_depth}"); //#DEBUG

        /* PST Debug */
        // Console.WriteLine($"Move: {GetPV()} PST Val (Move.To): {GetPstVal(GetPV().TargetSquare.Index, (int)GetPV().MovePieceType - 1, board.IsWhiteToMove, true)}"); //#DEBUG

        /* Search Debug */
        // Console.WriteLine(); //#DEBUG
        // var pv_moves = new Stack<Move>(); //#DEBUG
        // for (int i = 0; i < search_depth; i++) { //#DEBUG
        //     Console.WriteLine($"PV {GetPV()} static score: {Evaluation.Debug(board, packed_psts, piece_val, piece_phase, pawn_modifier), -5} depth: {i}"); //#DEBUG
        //     pv_moves.Push(GetPV()); //#DEBUG
        //     board.MakeMove(GetPV()); //#DEBUG
        // }
        // for (int i = 0; i < search_depth; i++) { //#DEBUG
        //     board.UndoMove(pv_moves.Pop()); //#DEBUG
        // } //#DEBUG

        Console.WriteLine($"PV {root_pv} TT {tt[board.ZobristKey].Item1} (bound: {tt[board.ZobristKey].Item3}) depth: {search_depth - 1, -2} nodes: {nodes,-6} quiesce nodes: {quiesce_nodes,-6} tt hits: {tt_hits, -5} delta: {timer.MillisecondsElapsedThisTurn}ms"); //#DEBUG

        if (!board.GetLegalMoves().Contains(root_pv)) { //#DEBUG
            Console.WriteLine(board.CreateDiagram()); //#DEBUG
            Console.WriteLine($"{root_pv}"); //#DEBUG
            Console.WriteLine($"is original position: {(root_key == board.ZobristKey)}"); //#DEBUG
            throw new Exception("ERROR: Trying to make move that doesn't exist"); //#DEBUG
        } //#DEBUG

        return root_pv;
    }


    /* SEARCH ---------------------------------------------------------------------------------- */
    private int NegaMax(int depth, int alpha, int beta)
    {   
        //if (depth > 0 && board.IsRepeatedPosition()) return 0;

        /* Get Transposition Values */
        if (tt.TryGetValue(board.ZobristKey, out var entry) && entry.Item4 >= search_depth - depth && /* is this needed? -> */ depth > 0) // Item1 -> score, Item2 -> bouond, Item3 -> depth
        {
            tt_hits++; //#DEBUG
            if (entry.Item3 == 0) return entry.Item2; // exact score
            if (entry.Item3 == 1 && entry.Item2 <= alpha) return alpha; // fail low
            if (entry.Item3 == 2 && entry.Item2 >= beta) return beta; // fail high
        }
        //tt.TryGetValue(board.ZobristKey, out var entry);

        /* Quiescence Search (delta pruning) */
        var q_search = depth >= search_depth;
        int score, move_idx = 0;
        if (q_search) {
            score = Eval();
            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }
        if (!q_search) nodes++; //#DEBUG
        else quiesce_nodes++; //#DEBUG

        /* Move Ordering */
        Move pv = default;
        Span<Move> moves = stackalloc Move[128];
        board.GetLegalMovesNonAlloc(ref moves, q_search && !board.IsInCheck());

        var move_scores = new int[128];
        foreach (Move move in moves)
            move_scores[move_idx++] = (
                move == entry.Item1 && entry.Item3 == 0 ? 0 // PV move
                : (move.IsPromotion && move.PromotionPieceType == PieceType.Queen) ? 1 // queen promotion
                : move.IsCapture ? 10 - (int)move.CapturePieceType + (int)move.MovePieceType // captures
                : move.IsCastles ? 2 //castles
                : 20 // other moves
            );
        move_scores.AsSpan(0, moves.Length).Sort(moves);

        // this avoids issues with with checkmate or stalemate only being seen in quiescence search
        if (moves.Length == 0) return board.IsInCheck() ? depth - 50000 : 0;

        /* Main Search */
        foreach (var move in moves)
        {
            board.MakeMove(move);
            score = -NegaMax(depth + 1, -beta, -alpha);
            board.UndoMove(move);

            if (score > alpha) {
                alpha = score;
                pv = move;
                if (depth == 0) root_pv = move;
            }
            if (score >= beta) break;

            if (timer.MillisecondsElapsedThisTurn > timeAllowed) return 11111;
        }

        /* Set Transposition Values */
        var best = Math.Min(alpha, beta);
        if (!q_search && entry.Item4 <= search_depth - depth) // only update TT if entry is shallower than current search depth
            tt[board.ZobristKey] = (
                pv,
                best,
                alpha >= beta ? 2 /* lower bound */
                : pv != default(Move) ? 0 /* exact bound */
                : 1, /* upper bound */
                search_depth - depth
            );

        return best;
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
        // if (board.IsDraw()) return 0;

        int score = 0,
            side_multiplier = board.IsWhiteToMove ? 1 : -1,
            pawns_count = 16 - board.GetAllPieceLists()[0].Count - board.GetAllPieceLists()[6].Count;

        int mg = 0, eg = 0, phase = 0;
        foreach (bool is_white in new[] { true, false }) //true = white, false = black (can likely be optimized for tokens if PSTs are changed)
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
        score += board.GetLegalMoves().Length;
        //board.ForceSkipTurn();
        //foreach (var move in board.GetLegalMoves()) if (move.MovePieceType != PieceType.Queen) score -= side_multiplier;
        score -= board.GetLegalMoves().Length;
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
    } //#DEBUG
}