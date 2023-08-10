using ChessChallenge.API;
using System;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private Dictionary<ulong, Move[]> moves_table = new();
    private int nodes;
    private int quiesce_nodes;

    private readonly int MAX_DEPTH = 3;

    public Move Think(Board board, Timer timer)
    {
        this.board = board;
        this.timer = timer;
        nodes = 0;
        quiesce_nodes = 0;

        NegaMax(0, -99999, 99999);
        Console.WriteLine($"nodes: {nodes,-8} quiesce nodes: {quiesce_nodes,-8} delta: {timer.MillisecondsElapsedThisTurn}ms");

        return GetOrderedLegalMoves()[0];
    }

    private int NegaMax(int depth, int alpha, int beta)
    {
        nodes++;
        if (depth == MAX_DEPTH) return Quiesce(alpha, beta);

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

        foreach (var move in GetOrderedLegalMoves())
        {
            board.MakeMove(move);
            int score = -Quiesce(-beta, -alpha);
            board.UndoMove(move);

            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }

        return alpha;
    }

    private int Eval()
    {
        var score = 0;
        foreach (var list in board.GetAllPieceLists())
        {
            var value = (int)list.TypeOfPieceInList switch
            {
                1 => 100,
                2 or 3 => 325,
                4 => 550,
                5 => 1000,
                _ => 50000,
            } * list.Count;

            if (!list.IsWhitePieceList) value = -value;
            score += value;
        }

        if (!board.IsWhiteToMove) score = -score;
        return score;
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

        Move temp_move = moves[0];
        for (int i = 0; i < pv_idx; i++)
        {
            moves[i] = temp_move;
            temp_move = moves[i + 1];
            moves[i + 1] = moves[i];
        }
        moves[0] = pv_move;
    }

    private Move[] GetOrderedLegalMoves() //{promotions, castles, captures, everything else}
    {
        if (moves_table.TryGetValue(board.ZobristKey, out var moves)) return moves;

        moves = board.GetLegalMoves();
        for (var i = 1; i < moves.Length; ++i)
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

        return moves;
    }

    private int GetPrecedence(Move move) //gets precedence of a move for move ordering {promotions, castles, captures, everything else}
    {
        return move.IsPromotion ? 0 : move.IsCastles ? 1 : move.IsCapture ? 2 : 3;
    }
}