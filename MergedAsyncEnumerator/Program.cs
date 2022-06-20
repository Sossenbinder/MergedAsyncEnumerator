using System.Diagnostics;

var sw = Stopwatch.StartNew();

var asyncIterators = Enumerable.Range(0, 3).Select(_ => Iterate());

foreach (var asyncIterator in asyncIterators)
{
	await foreach (var item in asyncIterator)
	{
		Console.WriteLine(item);
	}
}

sw.Stop();
Console.WriteLine(sw.Elapsed);
sw.Restart();

var asyncIteratorsMerged = new MergedAsyncEnumerable<int>(Enumerable.Range(0, 3).Select(_ => Iterate()).ToArray());

await foreach (var item in asyncIteratorsMerged)
{
	Console.WriteLine(item);
}

sw.Stop();
Console.WriteLine(sw.Elapsed);

async IAsyncEnumerable<int> Iterate()
{
	for (var i = 0; i < 5; ++i)
	{
		await Task.Delay(100);
		yield return i;
	}
}

public class MergedAsyncEnumerable<T> : IAsyncEnumerable<T>
{
	private readonly IAsyncEnumerable<T>[] _asyncEnumerables;

	public MergedAsyncEnumerable(params IAsyncEnumerable<T>[] asyncEnumerables)
	{
		_asyncEnumerables = asyncEnumerables;
	}

	public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
		=> ConsumeMergedAsyncEnumerabled().GetAsyncEnumerator(cancellationToken);

	private async IAsyncEnumerable<T> ConsumeMergedAsyncEnumerabled()
	{
		var iterators = _asyncEnumerables
			.Select((x, index) => new IndexedIterator(x, index))
			.ToArray();

		var tasks = iterators
			.Select(x => x.MoveAhead())
			.ToArray();

		while (tasks.Any(x => x is not null))
		{
			var winningTask = await Task.WhenAny(tasks.Where(x => x is not null));
			var (Item, HasMore, Index) = winningTask.Result;
			yield return Item;

			if (!HasMore)
			{
				tasks[Index] = null;
				continue;
			}

			tasks[Index] = iterators[Index].MoveAhead();
		}
	}

	private record IndexedIteratorResult(T Item, bool HasMore, int Index);

	private class IndexedIterator
	{
		private int _index;

		private readonly IAsyncEnumerator<T> _asyncEnumerator;

		public IndexedIterator(IAsyncEnumerable<T> asyncEnumerable, int index)
		{
			_asyncEnumerator = asyncEnumerable.GetAsyncEnumerator();
			_index = index;
		}

		public async Task<IndexedIteratorResult> MoveAhead()
		{
			var hasMoreEntries = await _asyncEnumerator.MoveNextAsync();
			return new IndexedIteratorResult(_asyncEnumerator.Current, hasMoreEntries, _index);
		}
	}
}