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
 */
public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private int time_allowed;

    private int search_depth;
    private int gamephase;

    private Move root_pv;

    private readonly int[] piece_val = { 0, 100, 325, 325, 550, 1000, 0 };
    private readonly int[] piece_phase = { 0, 0, 1, 1, 2, 4, 0 };

    private readonly (ulong, Move, int, int, int)[] tt = new (ulong, Move, int, int, int)[0x400000]; // (hash, move, score, depth_left, bound), bound -> 0 = exact, 1 = upper, 2 = lower
    private int[,,] history_table;

    private int[][] psts; //[64][12] (by-square first, then by-piece)
    public MyBot()
    {
        var piece = 1;
        psts = PstPacker.Generate()
                        .Select(packedTable => new System.Numerics.BigInteger(packedTable)
                            .ToByteArray()
                            .Take(12)
                            .Select(square => (sbyte)square * 2 + piece_val[piece++ % 6])
                            .ToArray())
                        .ToArray();
    }

    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;
        time_allowed = 2 * timer.MillisecondsRemaining / (35 + 1444 / (board.PlyCount / 2 /* <- # of full moves */ + 67));
        history_table = new int[2, 7, 64]; // [side_to_move][piece_type][square]

        search_depth = 2;
        for (int alpha = -99999, beta = 99999, fail_lows = 0, fail_highs = 0; ;)
        {
            int score = NegaMax(0, alpha, beta, true);
            var sec_elapsed = timer.MillisecondsElapsedThisTurn / 1000; //#DEBUG

            if (timer.MillisecondsElapsedThisTurn > time_allowed || score > 49000) return root_pv;

            /* Aspiration Windows */
            if (score <= alpha)
               alpha -= 65; // * ++fail_lows * fail_lows;
            else if (score >= beta)
               beta += 65; // * ++fail_highs * fail_highs;
            else
            {
                alpha = score - 45;
                beta = score + 45;
                //Console.WriteLine($"info depth {search_depth} time {timer.MillisecondsElapsedThisTurn} nodes {nodes}");
                search_depth++;
            }
        }
    }


    /* SEARCH ---------------------------------------------------------------------------------- */
    private int NegaMax(int depth, int alpha, int beta, bool allow_null)
    {
        if (depth > 0 && board.IsRepeatedPosition()) return 0;

        int score = Eval(),
            depth_left = search_depth - depth,
            move_idx = 0;

        /* Get Transposition Values */
        ref var entry = ref tt[board.ZobristKey & 0x3FFFFF];
        if (entry.Item1 == board.ZobristKey && entry.Item4 >= depth_left && /* is this needed? -> */ depth > 0
            && (entry.Item5 == 0
            || entry.Item5 == 1 && entry.Item3 <= alpha
            || entry.Item5 == 2 && entry.Item3 >= beta))
            return entry.Item3;

        /* Quiescence Search (delta pruning) */
        bool q_search = depth >= search_depth, can_f_prune = false;
        if (q_search)
        {
            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }
        else if (!board.IsInCheck() && (beta - alpha == 1/* || gamephase > 0*/))
        {

            /* Reverse Futility Pruning */
            //if (depth_left <= 8 && static_eval - 95 * depth >= beta) {
            //    rfp_count++;  
            //    return static_eval - 100 * depth; // fail soft
            //}

            /* Null Move Pruning */
            if (depth_left >= 2 && allow_null && gamephase > 0)
            {
                board.ForceSkipTurn();
                score = -NegaMax(depth + 3 + depth_left / 4, -beta, -alpha, false);
                board.UndoSkipTurn();
                if (score >= beta) return score; // fail soft
            }

            /* Extended Futility Pruning */
            if (depth_left <= 4 && score + 96 * depth <= alpha) can_f_prune = true;
        }

        /* Move Ordering */
        Move pv = default;
        Span<Move> moves = stackalloc Move[128];
        board.GetLegalMovesNonAlloc(ref moves, q_search/* && !board.IsInCheck()*/);

        // checking for checkmate / stalemate
        if (!q_search && moves.IsEmpty) return board.IsInCheck() ? depth - 50000 : 0;

        Span<int> move_scores = stackalloc int[moves.Length];
        foreach (Move move in moves)
            move_scores[move_idx++] = -(
                move == entry.Item2 && entry.Item5 == 0 ? 99999 // PV move
                : (move.IsPromotion && move.PromotionPieceType == PieceType.Queen) ? 99998 // queen promotion
                : move.IsCapture ? 77777 - (int)move.CapturePieceType + (int)move.MovePieceType // MVV-LVA
                : history_table[board.IsWhiteToMove ? 0 : 1, (int)move.MovePieceType, move.TargetSquare.Index] // history heuristic
            );
        MemoryExtensions.Sort(move_scores, moves);

        /* Main Search */
        move_idx = 0;
        foreach (var move in moves)
        {
            board.MakeMove(move);

            // don't prune captures, promotions, or checks (also ensure at least one move is searched)
            if (can_f_prune && !(move.IsCapture || board.IsInCheck() || move.IsPromotion || move_idx++ == 0))
            {
                board.UndoMove(move);
                continue;
            }

            score = -NegaMax(depth + 1, -beta, -alpha, true);
            board.UndoMove(move);

            if (score > alpha)
            {
                alpha = score;
                pv = move;
                if (depth == 0) root_pv = move;
            }
            if (score >= beta)
            {
                if (!move.IsCapture && gamephase > 0) history_table[board.IsWhiteToMove ? 0 : 1, (int)move.MovePieceType, move.TargetSquare.Index] += depth_left * depth_left;
                break;
            }

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
                : pv != default ? 0 /* exact bound */
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
     */
    private int Eval()
    {
        if (board.IsDraw()) return 0;

        int score,
            side_multiplier = board.IsWhiteToMove ? 1 : -1;

        int mg = 0, eg = 0;
        gamephase = 0;
        foreach (bool is_white in new[] { true, false }) //true = white, false = black (can likely be optimized for tokens if PSTs are changed)
        {
            for (var piece_type = PieceType.None; piece_type++ < PieceType.King;)
            {
                int piece = (int)piece_type;
                ulong mask = board.GetPieceBitboard(piece_type, is_white);
                while (mask != 0)
                {
                    int lsb = ClearAndGetIndexOfLSB(ref mask);
                    gamephase += piece_phase[piece];

                    // doesn't use piece value anymore since psts include piece value
                    mg += psts[lsb][piece - 1];
                    eg += psts[lsb][piece + 5];
                }
            };

            mg = -mg;
            eg = -eg;
        }

        //mg += 10 * side_multiplier; // tempo bonus
        score = (mg * gamephase + eg * (24 - gamephase)) / 24; // max gamephase = 24

        return score * side_multiplier;
    }
}