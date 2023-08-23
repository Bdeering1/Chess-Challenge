using ChessChallenge.API;

using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;

using static System.Math;
using static ChessChallenge.API.BitboardHelper;
using static System.Int32;

public class Tjalle : IChessBot
{
    //private const ulong FileA = 0x0101010101010101;
    //private const ulong FileH = 0x8080808080808080;

    /// <summary>
    /// Lookup tables for piece value on different squares.
    /// </summary>
    private readonly ulong[,] positionalLookups =
    {
        {
            0,
            42221890761523350,
            36592262375669870,
            35184844542115945,
            36592176475668580,
            28147884224348265,
            22518470590464105,
            0
        },
        {
            81628988804956430,
            90073366956605720,
            94295534558249250,
            95702930916966690,
            95702930916639010,
            94295534558576930,
            91480741840159000,
            81628988804956430
        },
        {
            90073366957916470,
            92888159675351360,
            95702930917294400,
            95702930917622080,
            95702952392130880,
            95702952392786240,
            92888159675679040,
            90073366957916470
        },
        {
            140739635871744500,
            143554428589179385,
            140739635871744495,
            140739635871744495,
            140739635871744495,
            140739635871744495,
            140739635871744495,
            142147010755297780
        },
        {
            251923926735258480,
            253331344569140090,
            254738740927529850,
            254738740927529855,
            254738740927529860,
            254738740927857530,
            253331366043976570,
            251923926735258480
        },
        {
            239257423932293990,
            239257423932293990,
            239257423932293990,
            239257423932293990,
            242072216649728880,
            247701759134270330,
            253331344570450840,
            253331387520779160
        }
    };

    private Board _board;

    private int _startPly;
    private Move _bestMove;

    int[] numNodes = new int[10]; // #DEBUG

    private const int TTSize = 1 << 20;
    private readonly Entry[] transpositiontable = new Entry[TTSize]; // Right now, this is 24 Mb, so could be larger if necessary.

    private const int exactFlag = 0,
                      alphaFlag = 1,
                      betaFlag  = 2; // These constants can be inlined to save tokens.

    /// <summary>
    /// Entry used for the transposition table
    /// </summary>
    private record struct Entry(ulong Zobrist, Move Bestmove, int Depth, int Score, int Flag);

    /// <summary>
    /// Make a move.
    /// </summary>
    /// <param name="board">The current board state.</param>
    /// <param name="timer">The current timer.</param>
    /// <returns>The best move according to the bot.</returns>
    public Move Think(Board board, Timer timer)
    {
        //LookupCompactor.Compact();
        _board = board;
        _startPly = board.PlyCount;

        //for (int i = 0; i < numNodes.Length; i++) numNodes[i] = 0; // #DEBUG

        int movesToGo = 40 - board.PlyCount / 2;
        if (movesToGo <= 0) movesToGo = 40;
        int alottedTime = timer.MillisecondsRemaining / movesToGo;

        Move previousBestMove = new(); // Having this separately allows for mid-search breaks.

        int depth;
        for (depth = 1; depth < 40; depth++)
        {
            int timeRemaining = alottedTime - timer.MillisecondsElapsedThisTurn;
            if (timer.MillisecondsElapsedThisTurn * 15 > timeRemaining) break;

            NegaMax(depth, -MaxValue, MaxValue,
                _board.GetAllPieceLists().Sum(list => (list.IsWhitePieceList == _board.IsWhiteToMove ? 1 : -1)
                    * list.Sum(piece => LookupPieceValue(piece.Square.Index, list.IsWhitePieceList, piece.PieceType))
                )
            );

            previousBestMove = _bestMove;
        }

        //Console.WriteLine(depth);

        //for (int i = 0; i < numNodes.Length; i++) Console.WriteLine($"depth: {i}, nodes: {numNodes[i]}"); // #DEBUG
        return previousBestMove;
    }

    /// <summary>
    /// Performs a minimax search with alpha-beta pruning.
    /// </summary>
    /// <param name="depth">The maximum search depth. A deprh of 0 or less means quiescent search.</param>
    /// <param name="alpha">The alpha value for pruning.</param>
    /// <param name="beta">The beta value for pruning.</param>
    /// <param name="partialEval">The material evaluation of the position.</param>
    /// <returns>The evaluation of the current position.</returns>
    int NegaMax(int depth, int alpha, int beta, int partialEval)
    {
        int originalAlpha = alpha;
        //numNodes[Max(depth, 0)]++; // #DEBUG

        // Can only occur during search, not at root, so setting _bestMove not required.
        if (_board.IsDraw()) return 0;
        if (_board.IsInCheckmate()) return MinValue + _board.PlyCount;

        int best = -MaxValue;

        ulong key = _board.ZobristKey;
        Entry entry = transpositiontable[key % TTSize];

        //Transposition check
        if (entry.Zobrist == key && entry.Depth >= depth)
        {
            if (_board.PlyCount == _startPly) _bestMove = entry.Bestmove;

            if (entry.Flag == exactFlag)
                return entry.Score;
            if (entry.Flag == alphaFlag && entry.Score <= alpha)
                return alpha;
            if (entry.Flag == betaFlag && entry.Score >= beta)
                return beta;
        }

        if (depth <= 0)
        {
            best = partialEval + Evaluate();
            if (best >= beta)
            {
                //transpositiontable[key % TTSize] = new Entry(key, _bestMove, depth, best, betaFlag);
                return best;
            }
            alpha = Max(alpha, best);
        }

        // Get legal moves (only captures if quiescent search).
        Span<Move> legalMoves = stackalloc Move[218];
        _board.GetLegalMovesNonAlloc(ref legalMoves, depth <= 0);

        int numMoves = legalMoves.Length;

        // Calculate material score change per move.
        // Positive is better for the side to move.
        Span<(int, int)> moveEvalUpdates = stackalloc (int, int)[numMoves];
        for (int i = 0; i < numMoves; i++)
        {
            Move move = legalMoves[i];
            var (startIndex, targetIndex, isWhite, movePieceType) = (move.StartSquare.Index, move.TargetSquare.Index, _board.IsWhiteToMove, move.MovePieceType);

            int score = LookupPieceValue(targetIndex, isWhite, movePieceType) - LookupPieceValue(startIndex, isWhite, movePieceType);

            if      (move.IsEnPassant) score += LookupPieceValue(startIndex + targetIndex % 8 - startIndex % 8, !isWhite, movePieceType);
            else if (move.IsCapture)   score += LookupPieceValue(targetIndex, !isWhite, move.CapturePieceType);
            else if (move.IsCastles)   score += LookupPieceValue(targetIndex + Sign(startIndex - targetIndex), isWhite, PieceType.Rook) - 500;
            // Rook in the corner is exactly 500 centipawns.

            if (move.IsPromotion) score += LookupPieceValue(targetIndex, isWhite, move.PromotionPieceType);

            moveEvalUpdates[i] = (entry.Bestmove == move ? 10_000 : score, score);
        }
        moveEvalUpdates.Sort(legalMoves); // Order moves by evaluation strength. Perhaps not ideal, but works well enough.

        Move bestMove = legalMoves.Length > 0 ? legalMoves[0] : new();

        // Higher value is better, sorted ascending, so reverse.
        while (numMoves --> 0)
        {
            Move move = legalMoves[numMoves];

            _board.MakeMove(move);
            int evaluation = -NegaMax(depth - 1, -beta, -alpha, -(partialEval + moveEvalUpdates[numMoves].Item2));
            _board.UndoMove(move);

            //if (_board.PlyCount == _startPly) Console.WriteLine($"{move}, {evaluation}"); // #DEBUG

            if (evaluation > best)
            {
                best = evaluation;
                bestMove = move;

                alpha = Max(alpha, evaluation);

                if (alpha >= beta) break;
            }
        }

        if (_board.PlyCount == _startPly) _bestMove = bestMove;

        //int flag = best >= beta ? betaFlag : best <= alpha ? alphaFlag : exactFlag;
        transpositiontable[key % TTSize] = new(key, bestMove, depth, best, best <= originalAlpha ? alphaFlag : best >= beta ? betaFlag : exactFlag);
        return best;
    }

    /// <summary>
    /// Performs static evaluation of the current position.
    /// Only calculates extra features. Material score is incrementally updated.
    /// </summary>
    /// <returns>An integer score. Positive means better for the player to move.</returns>
    int Evaluate()
    {
        return 0;
    }

    int LookupPieceValue(int index, bool isWhite, PieceType pieceType)
    {
        var (rank, file) = DivRem(isWhite ? index ^ 56 : index, 8);
        int offset = Min(file, 7 - file) * 16;

        return (int)((positionalLookups[(int)pieceType - 1, rank] & 0xFFFFul << offset) >> offset);
    }

    int KingSafetyDefendingPawns(bool white)
    {
        ulong kingAttacks = GetKingAttacks(_board.GetKingSquare(white));

        return GetNumberOfSetBits((white ? kingAttacks << 8 : kingAttacks >> 8) & _board.GetPieceBitboard(PieceType.Pawn, white)) * 30;
        // This should probably have higher value later, if we have something to counteract the fact that this gives pushing center pawns in the opening negatively affects score.
    }

    int KingSafetyStormingPawns(bool white)
    {
        ulong kingAttacks = GetKingAttacks(_board.GetKingSquare(white));

        return GetNumberOfSetBits(((white ? kingAttacks << 16 : kingAttacks >> 16) | kingAttacks) & _board.GetPieceBitboard(PieceType.Pawn, !white)) * -20;
    }
    // TODO: make sure to use also the king's square itself.
}