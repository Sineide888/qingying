namespace Qingying.Core;

public static class PlaylistLogic
{
    public static int Resolve(int current, int count, RepeatMode mode, PlaylistMove move)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0)
        {
            return move == PlaylistMove.Previous ? count - 1 : 0;
        }

        switch (move)
        {
            case PlaylistMove.Next:
                if (current + 1 < count)
                {
                    return current + 1;
                }

                return mode == RepeatMode.List ? 0 : -1;
            case PlaylistMove.Previous:
                if (current > 0)
                {
                    return current - 1;
                }

                return mode == RepeatMode.List ? count - 1 : 0;
            case PlaylistMove.Ended:
                if (mode == RepeatMode.One)
                {
                    return current;
                }

                if (current + 1 < count)
                {
                    return current + 1;
                }

                return mode == RepeatMode.List ? 0 : -1;
            default:
                return current;
        }
    }
}
