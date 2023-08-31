using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

using static ChessChallenge.API.BitboardHelper;

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
 * - possibly refactor packed PSTs to use decimals
 * - integrate piece values with PSTs
 * - add history heuristic
 * - add aspiration windows
 */
public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private int time_allowed;

    private int nodes; //#DEBUG
    private int quiesce_nodes; //#DEBUG
    private int tt_hits; //#DEBUG
    private int nmp_count; //#DEBUG
    private int rfp_count; //#DEBUG
    private int efp_count; //#DEBUG

    private int search_depth;
    private int gamephase;

    private Move root_pv;
    private ulong root_key; //#DEBUG
    
    private bool logged_side = false; //#DEBUG

    private readonly int[] piece_val = { 0, 100, 317 /* 325 - 8 */, 333 /* 325 + 8 */, 558 /* 550 + 8 */, 1000, 0 };
    private readonly int[] piece_phase = { 0, 0, 1, 1, 2, 4, 0 };
    private readonly int[] pawn_modifier = { 0, 0, 1, -1, -1, 0, 0 };

    private readonly (ulong, Move, int, int, int)[] tt = new (ulong, Move, int, int, int)[0x400000]; // (hash, move, score, depth_left, bound), bound -> 0 = exact, 1 = upper, 2 = lower

    private ulong[] packed_psts = PstPacker.Generate();

    public Move Think(Board _board, Timer _timer)
    {
        if (!logged_side) { if (_board.IsWhiteToMove && !logged_side) Console.WriteLine("Playing white"); else if (!logged_side) Console.WriteLine("Playing black"); logged_side = true; } //#DEBUG

        board = _board;
        timer = _timer;
        nodes = quiesce_nodes = tt_hits = nmp_count = rfp_count = 0; //#DEBUG
        time_allowed = 2 * timer.MillisecondsRemaining / (60 + 1444 / (board.PlyCount / 2 /* <- # of full moves */ + 27));

        root_key = board.ZobristKey; //#DEBUG

        //Console.WriteLine(); //#DEBUG
        search_depth = 2;
        for (int alpha = -99999, beta = 99999, fail_lows = 0, fail_highs = 0;;)
        {
            nodes = tt_hits = 0; //#DEBUG
            int score = NegaMax(0, alpha, beta, true);
            //if (score != 11111) final_score = score; //#DEBUG
            //Console.WriteLine($"PV {root_pv} score: {score, -5} depth: {search_depth} nodes: {nodes,-6} quiesce nodes: {quiesce_nodes,-8} tt hits: {tt_hits, -5} delta: {timer.MillisecondsElapsedThisTurn/* - reg_delta*/}ms"); //#DEBUG

            if (timer.MillisecondsElapsedThisTurn > time_allowed) {
                /* Timing Debug */
                //Console.WriteLine($"{$"{timer.MillisecondsElapsedThisTurn:0.##}ms", -8} avg: {$"{(timer.GameStartTimeMilliseconds - timer.MillisecondsRemaining) / ++moves:0}ms", -8} depth: {search_depth}"); //#DEBUG

                /* PST Debug */
                // Console.WriteLine($"Move: {root_pv} PST Val (Move.To): {GetPstVal(GetPV().TargetSquare.Index, (int)root_pv.MovePieceType - 1, board.IsWhiteToMove, true)}"); //#DEBUG

                Console.WriteLine($"Eval: {score,-6} PV {root_pv, -13} depth: {search_depth - 1,-3} nodes: {nodes,-6} quiesce nodes: {quiesce_nodes,-6} NMP: {nmp_count,-6} RFP: {rfp_count,-5} EFP: {efp_count,-5} fls: {fail_lows,-2} fhs: {fail_highs,-2} tt hits: {tt_hits, -6} delta: {timer.MillisecondsElapsedThisTurn}ms"); //#DEBUG

                if (!board.GetLegalMoves().Contains(root_pv)) { //#DEBUG
                    Console.WriteLine(board.CreateDiagram()); //#DEBUG
                    Console.WriteLine($"{root_pv}"); //#DEBUG
                    Console.WriteLine($"is original position: {(root_key == board.ZobristKey)}"); //#DEBUG
                    throw new Exception("ERROR: Trying to make move that doesn't exist"); //#DEBUG
                } //#DEBUG

                return root_pv;
            } //#DEBUG

            if (score <= alpha)
                alpha -= 60 * ++fail_lows * fail_lows;
            else if (score >= beta)
                beta += 60 * ++fail_highs * fail_highs;
            else {
                // set up aspiration window
                alpha = score - 25;
                beta = score + 25;
                search_depth++;
            }
        }
    }


    /* SEARCH ---------------------------------------------------------------------------------- */
    private int NegaMax(int depth, int alpha, int beta, bool allow_null)
    {   
        if (depth > 0 && board.IsRepeatedPosition()) return 0;

        int score,
            depth_left = search_depth - depth,
            move_idx = 0;

        /* Get Transposition Values */
        ref var entry = ref tt[board.ZobristKey & 0x3FFFFF];
        if (entry.Item1 == board.ZobristKey && entry.Item4 >= depth_left && /* is this needed? -> */ depth > 0
            && (entry.Item5 == 0
            || entry.Item5 == 1 && entry.Item3 <= alpha
            || entry.Item5 == 2 && entry.Item3 >= beta))
        {
            tt_hits++; //#DEBUG
            return entry.Item3;
        } //#DEBUG

        /* Quiescence Search (delta pruning) */
        bool q_search = depth >= search_depth, can_f_prune = false;
        if (q_search) {
            score = Eval();
            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }
        else if (!board.IsInCheck() && (beta - alpha == 1/* || gamephase > 0*/)) {
            var static_eval = Eval();

            /* Reverse Futility Pruning */
            if (depth_left <= 8 && static_eval - 95 * depth >= beta) {
                rfp_count++;
                return static_eval - 100 * depth; // fail soft
            }

            /* Null Move Pruning */
            if (depth_left >= 2 && allow_null && gamephase > 0) {
                board.ForceSkipTurn();
                score = -NegaMax(depth + 3 + depth / 4, -beta, -alpha, false); // why is the new depth calculated this way?
                board.UndoSkipTurn();
                if (score >= beta) {
                    nmp_count++; //#DEBUG
                    return score; // fail soft
                } //#DEBUG
            }

            /* Extended Futility Pruning */
            if (depth_left <= 5 && static_eval + 120 * depth <= alpha) can_f_prune = true;
        }
        if (!q_search) nodes++; //#DEBUG
        else quiesce_nodes++; //#DEBUG

        /* Move Ordering */
        Move pv = default;
        Span<Move> moves = stackalloc Move[128];
        board.GetLegalMovesNonAlloc(ref moves, q_search/* && !board.IsInCheck()*/);

        Span<int> move_scores = stackalloc int[moves.Length];
        foreach (Move move in moves)
            move_scores[move_idx++] = (
                move == entry.Item2 && entry.Item5 == 0 ? 0 // PV move
                : (move.IsPromotion && move.PromotionPieceType == PieceType.Queen) ? 1 // queen promotion
                : move.IsCapture ? 10 - (int)move.CapturePieceType + (int)move.MovePieceType // MVV-LVA
                : move.IsCastles ? 2 //castles
                : 20 // other moves
            );
        MemoryExtensions.Sort(move_scores, moves);

        // this avoids issues with with checkmate or stalemate only being seen in quiescence search
        if (!q_search && moves.IsEmpty) return board.IsInCheck() ? depth - 50000 : 0;

        /* Main Search */
        move_idx = 0;
        foreach (var move in moves)
        {
            board.MakeMove(move);

            // don't prune captures, promotions, or checks (also ensure at least one move is searched)
            if (can_f_prune && !(move.IsCapture || board.IsInCheck() || move.IsPromotion || move_idx++ == 0)) {
                efp_count++;
                board.UndoMove(move);
                continue;
            }

            score = -NegaMax(depth + 1, -beta, -alpha, true);
            board.UndoMove(move);

            if (score > alpha) {
                alpha = score;
                pv = move;
                if (depth == 0) root_pv = move;
            }
            if (score >= beta) break;

            if (depth > 2 && timer.MillisecondsElapsedThisTurn > time_allowed) return 55555;
        }

        /* Set Transposition Values */
        var best = Math.Min(alpha, beta);
        if (!q_search && entry.Item4 <= depth_left) // only update TT if entry is shallower than current search depth
            entry = (
                board.ZobristKey,
                pv,
                best,
                depth_left,
                alpha >= beta ? 2 /* lower bound */
                : pv != default(Move) ? 0 /* exact bound */
                : 1 /* upper bound */
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
     * 
     * Todo
     * --------------------------------------------------------------------------------------------------
     * - add king safety
     * - incremental eval?
     * - provision for timing out opponent?
     */
    private int Eval()
    {
        if (board.IsDraw()) return 0;

        int score = 0,
            side_multiplier = board.IsWhiteToMove ? 1 : -1,
            pawns_count = 16 - board.GetAllPieceLists()[0].Count - board.GetAllPieceLists()[6].Count;

        int mg = 0, eg = 0;
        gamephase = 0;
        foreach (bool is_white in new[] { true, false }) //true = white, false = black (can likely be optimized for tokens if PSTs are changed)
        {
            ulong attacks = 0;
            for (var piece_type = PieceType.None; piece_type++ < PieceType.King;)
            {
                int piece = (int)piece_type;//, idx;
                ulong mask = board.GetPieceBitboard(piece_type, is_white);
                while (mask != 0)
                {
                    int lsb = ClearAndGetIndexOfLSB(ref mask);
                    gamephase += piece_phase[piece];

                    mg += piece_val[piece] + pawn_modifier[piece] * pawns_count + GetPstVal(lsb, piece - 1, is_white, 0);
                    eg += piece_val[piece] * 2 + pawn_modifier[piece] * pawns_count + GetPstVal(lsb, piece - 1, is_white, 6);

                    attacks |= GetPieceAttacks(piece_type, new Square(lsb), 0, is_white);
                }
            };

            mg = -mg - GetNumberOfSetBits(GetKingAttacks(board.GetKingSquare(!is_white)) & attacks); // king safety score
            eg = -eg;
        }

        mg += 10 * side_multiplier; // tempo bonus
        score = (mg * gamephase + eg * (24 - gamephase)) / 24; // max gamephase = 24

        /* Mobility Score */
        // score += board.GetLegalMoves().Length;
        // board.ForceSkipTurn();
        // score -= board.GetLegalMoves().Length;
        // board.UndoSkipTurn();

        return score * side_multiplier;
    }

    private int GetPstVal(int lsb, int type, bool is_white, int table_shift, bool debug = false) // table_shift - should be either 0 (mg PSTs) or 6 (eg PSTs)
    {
        var file = lsb % 8;
        var rank = lsb / 8;
        if (debug) Console.WriteLine($"type: {type + 1} lsb: {lsb}, rank: {rank + 1}, file: {file + 1}, pst: {type * 4 + (file)}, pst_val: {(sbyte)((packed_psts[type * 4 + (file > 3 ? 7 - file : file)] >> (is_white ? 7 - rank : rank) * 8) & 0xFF)}"); //#DEBUG
        return (sbyte)((packed_psts[type * 4 + (file > 3 ? 7 - file : file) + table_shift] >> (is_white ? 7 - rank : rank) * 8) & 0xFF);
    }
}