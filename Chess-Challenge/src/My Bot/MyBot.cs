using ChessChallenge.API;
using System;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private List<int> scores;
    private int branches;
    private int orderedBranches;

    private int MAX_DEPTH = 4;

    public Move Think(Board board, Timer timer)
    {
        //make local vars to use in other functions
        this.board = board;
        this.timer = timer;
        this.branches = 0;
        this.orderedBranches = 0;

        scores = new();
        var sc = NegaMax(0, -99999, 99999);
        var score = NegaMaxOrdered(0, -99999, 99999);

        //Console.WriteLine(scores.Count);
        //foreach (var s in scores)
        //{
        //    Console.Write($"{s}, ");
        //}
        //Console.WriteLine();

        Console.WriteLine($"{branches}, {orderedBranches}, {sc==score}");
        var idx = 0;
        foreach (var s in scores)
        {
            if (s == score)
            {
                return board.GetLegalMoves()[idx];
            }
            idx++;
        }
        return board.GetLegalMoves()[0];
    }

    private int NegaMaxOrdered(int depth, int alpha, int beta)
    {
        orderedBranches++;
        if (depth == MAX_DEPTH) return Eval();

        var moves = board.GetLegalMoves();
        List<Move>[] movesSorted = { new List<Move>(0), new List<Move>(0), new List<Move>(0), new List<Move>(0) }; //{promotions, castles, captures, everything else}
        foreach (var m in moves)
        {
            var idx = 3;
            if (m.IsPromotion) {
                idx = 0;
            } else if (m.IsCastles)
            {
                idx = 1;
            } else if (m.IsCapture)
            {
                idx = 2;
            }
            movesSorted[idx].Add(m);
        }
        var moves_sorted = new List<Move>(1);
        moves_sorted.AddRange(movesSorted[0]);
        moves_sorted.AddRange(movesSorted[1]);
        moves_sorted.AddRange(movesSorted[2]);
        moves_sorted.AddRange(movesSorted[3]);

        foreach (var move in moves_sorted)
        {
            board.MakeMove(move);
            int score = -NegaMaxOrdered(depth + 1, -beta, -alpha);
            board.UndoMove(move);

            if (depth == 0) { scores.Add(score); }

            // no idea
            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }

        return alpha;
    }

    private int NegaMax(int depth, int alpha, int beta)
    {
        branches++;
        if (depth == MAX_DEPTH) return Eval();

        foreach (var move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            int score = -NegaMax(depth + 1, -beta, -alpha);
            board.UndoMove(move);

            if (depth == 0) { scores.Add(score); }

            // no idea
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

    private List<Move> GetOrderedLegalMoves()
    {

        return new List<Move>();
    }
}