using ChessChallenge.API;
using System;
using System.Collections.Generic;

/*
 * Development Resources
 * -------------------------------------------------------------------------------------------------
 * Move Ordering:                                       https://rustic-chess.org/search/ordering/reason.html
 * 
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
 *  
 *  Notes:
 *  - implement packed PSTs
 */
public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private Dictionary<ulong, Move[]> moves_table = new();
    private int nodes;
    private int quiesce_nodes;

    private int search_depth = 1;
    private readonly int MAX_DEPTH = 10;

    private bool logged_side = false;

    private double moves = 0;

    //possibly add constructor to test computer speed?

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
        nodes = 0;
        quiesce_nodes = 0;

        //Console.WriteLine("\nRegular search:");
        //search_depth = MAX_DEPTH;
        //moves_table = new();
        //NegaMax(0, -99999, 99999);
        //var reg_delta = timer.MillisecondsElapsedThisTurn;
        //Console.WriteLine($"depth: {search_depth} nodes: {nodes,-8} quiesce nodes: {quiesce_nodes,-8} delta: {reg_delta}ms");

        //Console.WriteLine("\nIterative deepening:");
        search_depth = 1;
        //moves_table = new();
        int startTime = 0;
        while (search_depth <= MAX_DEPTH)
        {
            nodes = 0;
            quiesce_nodes = 0;
            NegaMax(0, -99999, 99999);
            //Console.WriteLine($"depth: {search_depth} nodes: {nodes,-8} quiesce nodes: {quiesce_nodes,-8} delta: {timer.MillisecondsElapsedThisTurn/* - reg_delta*/}ms");
            search_depth++;

            //if the next iteration will take too much time, skip it
            if (getTimeForNextDepth((double)(timer.MillisecondsElapsedThisTurn - startTime) / (nodes+quiesce_nodes))/2 + timer.MillisecondsElapsedThisTurn > getTimeAllowance()) break;
            startTime = timer.MillisecondsElapsedThisTurn;
        }

        moves++;
        Console.WriteLine($"{timer.MillisecondsElapsedThisTurn:0.##}, {((timer.GameStartTimeMilliseconds-timer.MillisecondsRemaining) / moves):0.##}, {search_depth}");
        
        return GetOrderedLegalMoves()[0];
    }


    private int NegaMax(int depth, int alpha, int beta)
    {
        nodes++;
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

        if (pv.HasValue) SetPV(pv.Value);
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


    private void SetPV(Move pv_move)
    {
        var moves = GetOrderedLegalMoves();
        var pv_idx = 0;
        foreach (var m in moves)
        {
            if (m == pv_move) break;
            pv_idx++;
        }

        var i = 0;
        while (i < pv_idx)
        {
            moves[i + 1] = moves[i];
            i++;
        }
        moves[0] = pv_move;
    }

    private int getTimeForNextDepth(double timePerNode)
    {

        return (int)(Math.Pow(nodes + quiesce_nodes, 1.3) * timePerNode);
    }

    private int getTimeAllowance() //TODO: make this change based on opponent time left
    {
        return timer.GameStartTimeMilliseconds / 40; //average moves in a chess game is 40
    }

    private Move[] GetOrderedLegalMoves() //{promotions, castles, captures, everything else}
    {
        if (moves_table.TryGetValue(board.ZobristKey, out var moves)) return moves;

        moves = board.GetLegalMoves();
        for (var i = 1; i < moves.Length; i++)
        {
            //convert move type into number for sorting
            int precedence = GetPrecedence(moves[i]);
            //store the original element for inserting later
            var move = moves[i];
            int j = i - 1;

            //go down the array, swapping until we reach a spot where we can insert
            while (j >= 0 && GetPrecedence(moves[j]) > precedence)
            {
                moves[j + 1] = moves[j];
                j--;
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

        //possibly add check of target square to value farther up pieces better?

        //queen promotions are worth the most
        if (move.IsPromotion && move.PromotionPieceType == PieceType.Queen) return 0;
        //captures are ordered by the value of the piece they take
        if (move.IsCapture) return 10 - (int)move.CapturePieceType + (int)move.MovePieceType;
        //castles are worth 2nd most, after queen promotions
        if (move.IsCastles) return 1;
        //everything else is just put after that
        return 20;

        //return move.IsPromotion ? 0 : move.IsCastles ? 1 : move.IsCapture ? 2 : 3;
    }


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

        var score = 0;
        var captured_pawns = 16 - board.GetAllPieceLists()[0].Count - board.GetAllPieceLists()[6].Count;

        foreach (var list in board.GetAllPieceLists())
        {
            /* Material score */
            var piece_type = (int)list.TypeOfPieceInList;
            score += list.IsWhitePieceList ? 1 : -1 * piece_type switch
            {
                1 => 100 + GetPawnScore(list),
                // knight increased in value the more pawns there are
                2 => 333 /*(325 + 8)*/ - list.Count * captured_pawns,
                3 => 325,
                // rooks increase in value the fewer pawns there are
                4 => 542 /*(550 - 8)*/ + list.Count * captured_pawns,
                // queens increase in value the fewer pawns there are
                5 => 1000 + list.Count * captured_pawns - BitboardHelper.GetNumberOfSetBits(BitboardHelper.GetSliderAttacks(PieceType.Queen, list[0].Square, board)),
                _ => 0,
            } * list.Count;
        }

        /* Mobility Score */
        var side_multiplier = board.IsWhiteToMove ? 1 : -1;
        score += side_multiplier * GetOrderedLegalMoves().Length;
        board.ForceSkipTurn();
        score += -side_multiplier * GetOrderedLegalMoves().Length;
        board.UndoSkipTurn();

        score *= board.IsWhiteToMove ? 1 : -1;
        return score;
    }

    private int GetPawnScore(PieceList pawn_list)
    {
        var score = 0;
        //var pawns = board.GetPieceBitboard(PieceType.Pawn, pawn_list.IsWhitePieceList);
        //int pawn_idx;
        //while ((pawn_idx = BitboardHelper.ClearAndGetIndexOfLSB(ref pawns)) != 64)
        //{
        //    score += (64 - pawn_idx) / 4; // not sure if this is right
        //}

        return score;
    }
}