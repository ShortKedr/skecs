namespace SKECS;

public interface IQueryChunkProcessor<T1>
    where T1 : unmanaged
{
    void Execute(QueryChunkView<T1> chunk);
}

public interface IQueryChunkProcessor<T1, T2>
    where T1 : unmanaged
    where T2 : unmanaged
{
    void Execute(QueryChunkView<T1, T2> chunk);
}

public interface IQueryChunkProcessor<T1, T2, T3>
    where T1 : unmanaged
    where T2 : unmanaged
    where T3 : unmanaged
{
    void Execute(QueryChunkView<T1, T2, T3> chunk);
}
