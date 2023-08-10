using ChessChallenge.API;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private List<int> scores;
    private int nodes;
    private int quiesce_nodes;

    private int MAX_DEPTH = 3;

    public Move Think(Board board, Timer timer)
    {
        this.board = board;
        this.timer = timer;
        nodes = 0;
        quiesce_nodes = 0;

        scores = new();
        var score = NegaMax(0, -99999, 99999);

        var idx = 0;
        foreach (var s in scores)
        {
            if (s == score) return GetOrderedLegalMoves()[idx];
            idx++;
        }
        return GetOrderedLegalMoves()[0];
    }

    private int NegaMax(int depth, int alpha, int beta)
    {
        nodes++;
        if (depth == MAX_DEPTH) return Quiesce(alpha, beta);

        foreach (var move in GetOrderedLegalMoves())
        {
            board.MakeMove(move);
            int score = -NegaMax(depth + 1, -beta, -alpha);
            board.UndoMove(move);

            if (depth == 0) scores.Add(score);

            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }

        return alpha;
    }

    private int Quiesce(int alpha, int beta)
    {
        quiesce_nodes++;
        int stand_pat = Eval();
        if (stand_pat >= beta) return beta;
        if (stand_pat > alpha) alpha = stand_pat;

        foreach (var move in board.GetLegalMoves())
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

    private Move[] GetOrderedLegalMoves() //{promotions, castles, captures, everything else}
    {
        var moves = board.GetLegalMoves();
        for (var i = 1; i < moves.Length; ++i)
        {
            //convert move type into number for sorting
            int key = GetVal(moves[i]);
            //store the original element for inserting later
            var k = moves[i];
            int j = i - 1;

            //go down the array, swapping until we reach a spot where we can insert
            while (j>=0 && GetVal(moves[j]) > key)
            {
                moves[j + 1] = moves[j];
                j--;
            }
            moves[j + 1] = k; //insert
        }

        return moves;
    }

    private int GetVal(Move move) //gets "value" of a move, for move ordering {promotions, castles, captures, everything else}
    {
        return move.IsPromotion ? 0 : move.IsCastles ? 1 : move.IsCapture ? 2 : 3;
    }
}