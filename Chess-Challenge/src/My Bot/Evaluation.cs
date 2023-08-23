using ChessChallenge.API;
using System;

public class Evaluation {

    public static int Debug(Board board, ulong[] packed_psts, int[] piece_val, int[] piece_phase, int[] pawn_modifier) {
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

                    mg += piece_val[piece] + pawn_modifier[piece] * pawns_count + GetPstVal(packed_psts, lsb, piece - 1, is_white, 0);
                    eg += piece_val[piece] + pawn_modifier[piece] * pawns_count + GetPstVal(packed_psts, lsb, piece - 1, is_white, 6);
                }
            }

            mg = -mg;
            eg = -eg;
        }

        mg += 10 * side_multiplier;
        score = (mg * phase + eg * (24 - phase)) / 24; // max phase = 24

        /* Mobility Score */
        //foreach (var move in board.GetLegalMoves()) if (move.MovePieceType != PieceType.Queen) score += side_multiplier;
        //score += GetOrderedLegalMoves().Length;
        board.ForceSkipTurn();
        //foreach (var move in board.GetLegalMoves()) if (move.MovePieceType != PieceType.Queen) score -= side_multiplier;
        //score -= GetOrderedLegalMoves().Length;
        board.UndoSkipTurn();

        return score * side_multiplier;
    }

    private static int GetPstVal(ulong[] packed_psts, int lsb, int type, bool is_white, int table_shift, bool debug = false) // table_shift - should be either 0 (mg PSTs) or 6 (eg PSTs)
    {
        var file = lsb % 8;
        var rank = lsb / 8;
        if (debug) Console.WriteLine($"type: {type + 1} lsb: {lsb}, rank: {rank + 1}, file: {file + 1}, pst: {type * 4 + (file)}, pst_val: {(sbyte)((packed_psts[type * 4 + (file > 3 ? 7 - file : file)] >> (is_white ? 7 - rank : rank) * 8) & 0xFF)}"); //#DEBUG
        return (sbyte)((packed_psts[type * 4 + (file > 3 ? 7 - file : file) + table_shift] >> (is_white ? 7 - rank : rank) * 8) & 0xFF);
    }
}