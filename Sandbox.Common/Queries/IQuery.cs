namespace Sandbox.Common.Queries;

public interface IQuery<TResult>;

public interface IQueryHandler<in TQuery, out TResult> where TQuery : IQuery<TResult>
{
    TResult Request(TQuery query);
}