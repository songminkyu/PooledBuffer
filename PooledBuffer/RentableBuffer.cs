
using System.Buffers;
using System.Text;

namespace Buffer
{
    /// <summary>
    /// ArrayPool에 할당 받아서 처리하는 Buffer Interface
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IRentedBuffer<T> : IDisposable
    {
        /// <summary>
        /// 버퍼가 읽기 전용인지 여부를 반환합니다.
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// 현재 읽기 위치를 반환합니다.
        /// </summary>
        int ReaderIndex { get; }
        /// <summary>
        /// 현재 쓰기 위치를 반환합니다.
        /// </summary>
        int WriterIndex { get; }

        /// <summary>
        /// 버퍼가 끝에 도달했는지 여부를 반환합니다.
        /// </summary>
        bool IsEndOfBuffer { get; }
        /// <summary>
        /// 버퍼가 쓰기 가능한지 여부를 반환합니다.
        /// </summary>
        bool IsWritable { get; }
        /// <summary>
        /// 읽을 수 있는 메모리가 남아 있는지 확인합니다.
        /// </summary>
        bool IsReadable { get; }
        /// <summary>
        /// 버퍼가 해제되었는지 여부를 반환합니다.
        /// </summary>
        bool IsDisposed { get; }
        /// <summary>
        /// 현재 버퍼의 쓰기 가능한 공간을 반환합니다.
        /// </summary>
        int WritableBytes { get; }
        /// <summary>
        /// 남아있는 메모리의 크기를 반환합니다.
        /// </summary>
        int ReadableBytes { get; }
        /// <summary>
        /// 전체 데이터 크기를 반환합니다.
        /// </summary>
        int MaxCapacity { get; }

        /// <summary>
        /// 현재 사용가능한 읽기 전용 메모리 영역을 반환합니다.
        /// </summary>
        Memory<T> Memory { get; }

        /// <summary>
        /// 요청된 크기만큼 메모리 오프셋을 이동합니다.
        /// </summary>
        /// <param name="bufSize">이동할 크기</param>
        void Advance(int bufSize);

        /// <summary>
        /// 지정된 위치의 메모리를 반환합니다.
        /// </summary>
        /// <param name="range">범위</param>
        /// <returns>메모리 영역</returns>
        Memory<T> ReadMemory(Range range);

        /// <summary>
        /// 메모리를 초기 상태로 되돌립니다.
        /// </summary>
        void Reset();

        /// <summary>
        /// 지정된 위치에 데이터를 씁니다.
        /// </summary>
        /// <param name="data">쓸 데이터</param>
        /// <param name="destinationIndex">쓰기 시작할 위치</param>
        /// <param name="isFlush">자동으로 Advance를 수행 할지 여부</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        void Write(ReadOnlyMemory<T> data, int destinationIndex = 0, bool isFlush = false);

        /// <summary>
        /// 현재 오프셋 위치에 데이터를 씁니다.
        /// </summary>
        /// <param name="data">쓸 데이터</param>
        /// <param name="isFlush">자동으로 Advance를 수행 할지 여부</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        void WriteAtOffset(ReadOnlyMemory<T> data, bool isFlush = false);
        /// <summary>
        /// 특정 영역에 대하여 Array View를 얻습니다.
        /// </summary>
        /// <param name="range">범위</param>
        /// <returns></returns>
        ArraySegment<T> this[Range range] { get; }
        /// <summary>
        /// 배열 접근자
        /// </summary>
        /// <param name="index">인덱스</param>
        /// <returns></returns>
        T this[Index index] { get; set; }
    }
    /// <summary>
    /// 배열 풀을 관리하는 제네릭 정적 클래스입니다.
    /// 크기에 따라 두 가지 풀(작은 배열용, 큰 배열용)을 사용하여 메모리 할당을 최적화합니다.
    /// </summary>
    /// <typeparam name="T">배열에 저장될 요소의 타입</typeparam>
    /// <remarks>
    /// 이 클래스는 다음과 같은 특징을 가집니다:
    /// <list type="bullet">
    /// <item>작은 배열(≤1024)과 큰 배열(>1024)에 대해 서로 다른 풀을 사용</item>
    /// <item>빈 배열에 대해 공유된 단일 인스턴스 제공</item>
    /// <item>배열의 대여(Rent)와 반환(Return) 기능 지원</item>
    /// </list>
    /// </remarks>
    public static class ArrayPoolAllocator<T>
    {
        /// <summary>
        /// 비어 있는 배열을 나타내는 정적 읽기 전용 필드입니다.
        /// Array.Empty{T}()를 사용하여 타입 안전한 빈 배열을 제공합니다.
        /// 이 배열은 공유되며 불변이므로 메모리를 효율적으로 사용할 수 있습니다.
        /// </summary>

        public static readonly T[] Empty = Array.Empty<T>();
        /// <summary>
        /// 작은 배열 풀에서 관리할 수 있는 최대 배열 길이입니다.
        /// 이 크기를 초과하는 배열은 공유 풀(LargePool)에서 관리됩니다.
        /// </summary>
        private const int SmallPoolMaxLength = 1024;
        /// <summary>
        /// 작은 배열 풀의 각 버킷당 최대 배열 개수입니다.
        /// 메모리 사용량을 제한하면서도 효율적인 재사용을 가능하게 합니다.
        /// </summary>
        private const int SmallPoolMaxArraysPerBucket = 50;
        /// <summary>
        /// 작은 크기의 배열을 관리하는 전용 풀입니다.
        /// 제한된 크기와 버킷당 배열 수를 가지며, 자주 사용되는 작은 배열의 재사용을 최적화합니다.
        /// </summary>
        private static readonly ArrayPool<T> SmallPool = ArrayPool<T>.Create(maxArrayLength: SmallPoolMaxLength, maxArraysPerBucket: SmallPoolMaxArraysPerBucket);
        /// <summary>
        /// 큰 크기의 배열(>1024)을 관리하는 전역 공유 풀입니다.
        /// .NET의 기본 ArrayPool을 사용하여 큰 배열의 할당을 관리합니다.
        /// </summary>
        private static readonly ArrayPool<T> LargePool = ArrayPool<T>.Shared;
        /// <summary>
        /// 지정된 길이의 배열을 풀에서 대여합니다.
        /// </summary>
        /// <param name="length">필요한 배열의 길이</param>
        /// <returns>
        /// - length가 0인 경우: Empty 배열 반환
        /// - length가 1024 이하인 경우: SmallPool에서 배열 대여
        /// - length가 1024 초과인 경우: LargePool에서 배열 대여
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">length가 0보다 작은 경우 발생</exception>
        public static T[] Rent(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive");
            // 길이가 0인 경우 Empty 배열 반환.
            return length == 0 ? Empty : length <= SmallPoolMaxLength ? SmallPool.Rent(length) : LargePool.Rent(length);
        }
        /// <summary>
        /// 대여한 배열을 해당하는 풀로 반환합니다.
        /// </summary>
        /// <param name="array">반환할 배열</param>
        /// <param name="clearArray">
        /// true인 경우 반환 전에 배열의 모든 요소를 기본값으로 초기화합니다.
        /// 민감한 데이터를 다룰 때 이 옵션을 사용하세요.
        /// </param>
        /// <exception cref="ArgumentNullException">array가 null인 경우 발생</exception>
        /// <remarks>
        /// - Empty 배열이나 길이가 0인 배열은 풀로 반환되지 않습니다.
        /// - 배열은 원래 대여된 풀(SmallPool 또는 LargePool)로 반환됩니다.
        /// </remarks>
        public static void Return(T[] array, bool clearArray)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            // Empty 배열은 반환하지 않음
            if (array.Length == 0 || ReferenceEquals(array, Empty))
                return;
            var pool = array.Length <= SmallPoolMaxLength ? SmallPool : LargePool;
            pool.Return(array, clearArray);
        }
    }
    public class RentedBuffer<T> : IRentedBuffer<T>, IDisposable
    {
        /// <summary>
        /// 비어 있는 배열
        /// </summary>
        public static readonly IRentedBuffer<T> Empty = new RentedBuffer<T>(0);
#if DEBUG
        // 디버깅을 위한 추적 정보
        private readonly string _allocationStack;
        private string? _disposalStack;
#endif

        private T[]? _array;
        private int _offset;
        private int _length;
        private int _disposed;
        private readonly bool _isWritableBuffer;
        private readonly object _writeLock = new();
        private readonly bool _isTakeOwnership;
        /// <inheritdoc/>
        public bool IsReadOnly => !_isWritableBuffer;

        /// <inheritdoc/>
        protected int CurrentOffset
        {
            get
            {
                ThrowIfDisposed();
                return Volatile.Read(ref _offset);
            }
        }
        /// <inheritdoc/>
        protected int CurrentLength
        {
            get
            {
                ThrowIfDisposed();
                return Volatile.Read(ref _length);
            }
        }
        /// <inheritdoc/>
        public int ReadableBytes => WriterIndex - ReaderIndex;
        /// <inheritdoc/>
        public int ReaderIndex => CurrentOffset;
        /// <inheritdoc/>
        public int WriterIndex => CurrentLength;
        /// <inheritdoc/>
        public int WritableBytes
        {
            get
            {
                ThrowIfDisposed();
                if (!_isWritableBuffer)
                {
                    return 0; // 읽기 전용 버퍼는 쓰기 가능 공간이 없음
                }

                return Math.Max(0, MaxCapacity - CurrentLength);
            }
        }
        /// <inheritdoc/>
        public bool IsEndOfBuffer => CurrentOffset >= CurrentLength;

        /// <inheritdoc/>
        public bool IsWritable => _isWritableBuffer && !IsDisposed;

        /// <inheritdoc/>
        public bool IsReadable => ReadableBytes > 0;

        /// <inheritdoc/>
        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        /// <inheritdoc/>
        public Memory<T> Memory
        {
            get
            {
                ThrowIfDisposed();
                return _array == null ? throw new ObjectDisposedException(GetType().Name) : _array.AsMemory();
            }
        }
        /// <inheritdoc/>
        public T this[Index index]
        {
            get
            {
                ThrowIfDisposed();

                if (_array == null)
                    throw new ObjectDisposedException(GetType().Name);

                var actualIndex = index.IsFromEnd ? _length - index.Value : index.Value;

                return actualIndex < 0 || actualIndex >= _length ? throw new ArgumentOutOfRangeException(nameof(index)) : _array[actualIndex];
            }
            set
            {
                ThrowIfDisposed();

                if (!_isWritableBuffer)
                    throw new InvalidOperationException("Buffer is read-only");

                if (_array == null)
                    throw new ObjectDisposedException(GetType().Name);

                var actualIndex = index.IsFromEnd ? _length - index.Value : index.Value;

                if (actualIndex < 0 || actualIndex >= _length)
                    throw new ArgumentOutOfRangeException(nameof(index));

                lock (_writeLock)
                {
                    _array[actualIndex] = value;
                }
            }
        }
        /// <inheritdoc/>
        public ArraySegment<T> this[Range range]
        {
            get
            {
                ThrowIfDisposed();

                if (_array == null)
                    throw new ObjectDisposedException(GetType().Name);

                var (offset, length) = range.GetOffsetAndLength(_length);
                return offset < 0 || length < 0 || offset + length > MaxCapacity
                    ? throw new IndexOutOfRangeException("offset or length is out of range.")
                    : new ArraySegment<T>(_array, offset, length);
            }
        }
        /// <inheritdoc/>
        public int MaxCapacity { get; }

        private RentedBuffer(int maxCapacity, bool isWritable)
        {
            MaxCapacity = maxCapacity;
            _array = ArrayPoolAllocator<T>.Rent(maxCapacity);
            _offset = 0;
            _length = isWritable ? 0 : maxCapacity;
            _isWritableBuffer = isWritable;

#if DEBUG
            _allocationStack = Environment.StackTrace;
#endif
        }
        /// <summary>
        /// ReadOnlySpan으로부터 RentedBuffer를 초기화합니다.
        /// </summary>
        /// <param name="other">복사할 ReadOnlySequence</param>
        /// <exception cref="ArgumentException">sequence의 길이가 int.MaxValue를 초과하거나 0 이하인 경우</exception>
        public RentedBuffer(ReadOnlySpan<T> other)
            : this(other.Length <= 0
                ? throw new ArgumentException("Length must be positive.", nameof(other))
                : other.Length, false)
        {
            other.CopyTo(_array);
        }
        /// <summary>
        /// 기존 배열을 사용하여 RentedBuffer를 초기화합니다.
        /// </summary>
        /// <param name="array">사용할 배열</param>
        public RentedBuffer(T[] array) : this(array.AsSpan())
        {
        }
        /// <inheritdoc/>
        public RentedBuffer(ReadOnlyMemory<T> memory) : this(memory.Span)
        {
        }

        /// <summary>
        /// ReadOnlySequence로부터 RentedBuffer를 초기화합니다.
        /// </summary>
        /// <param name="sequence">복사할 ReadOnlySequence</param>
        /// <exception cref="ArgumentException">sequence의 길이가 int.MaxValue를 초과하거나 0 이하인 경우</exception>
        public RentedBuffer(ReadOnlySequence<T> sequence)
            : this(
                sequence.Length > int.MaxValue
                    ? throw new ArgumentException("Sequence is too large to process", nameof(sequence))
                    : sequence.Length <= 0
                        ? throw new ArgumentException("Sequence length must be positive.", nameof(sequence))
                        : (int)sequence.Length,
                false)
        {


            if (sequence.IsSingleSegment)
            {
                sequence.First.Span.CopyTo(_array);
            }
            else
            {
                sequence.CopyTo(_array);
            }
        }
        /// <inheritdoc/>
        public RentedBuffer(int length)
            : this(length <= 0
                ? throw new ArgumentException("Length must be positive.", nameof(length))
                : length, true)
        {
        }
        /// <summary>
        /// Rented Buffer 소멸자
        /// </summary>
        ~RentedBuffer()
        {
            Dispose(false);
        }
        /// <inheritdoc/>
        public Memory<T> ReadMemory(Range range)
        {

            ThrowIfDisposed();

            var segment = this[range];

            return new Memory<T>(segment.Array!, segment.Offset, segment.Count);
        }
        private void CopyDataInternal(ReadOnlyMemory<T> data, int destinationIndex)
        {
            const int PARALLEL_THRESHOLD = 1024 * 1024;   // 1MB
            const int MIN_SLICE_SIZE = 32 * 1024;        // 32KB minimum chunk size
            lock (_writeLock)
            {
                // 병렬 복사를 위한 적절한 청크 크기 계산
                int maxChunks = Math.Max(1, data.Length / MIN_SLICE_SIZE);
                int maxDegreeOfParallelism = Math.Min(
                    Environment.ProcessorCount,
                    maxChunks
                );

                // 데이터가 작거나 병렬화가 이점이 없는 경우
                if (data.Length < PARALLEL_THRESHOLD || maxDegreeOfParallelism <= 1)
                {
                    data.CopyTo(_array.AsMemory(destinationIndex));
                }
                else
                {
                    int sliceSize = (int)Math.Min(
                        Math.Max(MIN_SLICE_SIZE, data.Length / maxDegreeOfParallelism),
                        int.MaxValue
                    );

                    try
                    {
                        int chunks = (int)((long)data.Length + sliceSize - 1) / sliceSize;
                        ParallelLoopResult result = Parallel.For(0, chunks,
                            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                            i =>
                            {
                                long start = (long)i * sliceSize;
                                if (start < data.Length)
                                {
                                    int length = (int)Math.Min(sliceSize, data.Length - start);
                                    data.Slice((int)start, length)
                                        .CopyTo(_array.AsMemory(destinationIndex + (int)start));
                                }
                            });

                        if (!result.IsCompleted)
                        {
                            throw new InvalidOperationException("Parallel copy operation was not completed successfully.");
                        }
                    }
                    catch (AggregateException)
                    {
                        throw;
                    }
                }
                // 버퍼 길이 업데이트 (오버플로우 방지)
                long newLength = (long)destinationIndex + data.Length;
                _length = (int)Math.Min(Math.Max(_length, newLength), int.MaxValue);
            }
        }
        /// <inheritdoc/>
        public void Write(ReadOnlyMemory<T> data, int destinationIndex = 0, bool isFlush = false)
        {
            ThrowIfDisposed();

            if (!_isWritableBuffer)
            {
                throw new InvalidOperationException("This buffer was initialized as read-only buffer.");
            }

            // 정수 오버플로우 방지를 위한 체크
            if (destinationIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationIndex), "Destination index cannot be negative.");
            }

            if (data.Length > MaxCapacity - destinationIndex)  // 오버플로우 없이 체크
            {
                throw new ArgumentOutOfRangeException(nameof(destinationIndex), "Data exceeds buffer capacity.");
            }

            CopyDataInternal(data, destinationIndex);
            if (isFlush)
            {
                Advance(data.Length);
            }
        }

        /// <inheritdoc/>
        public void WriteAtOffset(ReadOnlyMemory<T> data, bool isFlush = false)
        {
            Write(data, CurrentOffset, isFlush);
        }
        /// <inheritdoc/>
        public void Advance(int bufSize)
        {
            ThrowIfDisposed();
            if (bufSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufSize), "Advance size must be positive.");
            }

            const int maxRetires = 100;

            int retryCount = 0;
            while (retryCount < maxRetires)
            {
                var currentOffset = CurrentOffset;

                if (currentOffset >= MaxCapacity)
                {
                    throw new InvalidOperationException("Buffer is at the end.");
                }

                // 읽을 수 있는 바이트 수 계산
                var readableBytes = ReadableBytes;
                if (bufSize > readableBytes)
                {
                    throw new ArgumentOutOfRangeException(nameof(bufSize),
                        $"Cannot advance beyond readable bytes. Available: {readableBytes}, Requested: {bufSize}");
                }
                // 오버 플로우 체크
                if (bufSize > MaxCapacity - currentOffset)
                {
                    throw new InvalidOperationException("Advancing would cause integer overflow.");
                }

                var newOffset = currentOffset + bufSize;
                if (newOffset > MaxCapacity)
                {
                    newOffset = MaxCapacity;
                }
                // offset만 CAS로 업데이트
                if (Interlocked.CompareExchange(ref _offset, newOffset, currentOffset) == currentOffset)
                {
                    return;
                }
                retryCount++;
                Thread.SpinWait(1 << Math.Min(retryCount, 30)); // 지수 백오프
            }
            throw new InvalidOperationException("Failed to advance buffer after maximum retries.");
        }
        ///<inheritdoc/>
        public void Reset()
        {
            ThrowIfDisposed();
            lock (_writeLock)
            {
                Volatile.Write(ref _offset, 0);
                Volatile.Write(ref _length, _isWritableBuffer ? 0 : MaxCapacity);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
        /// <summary>
        /// 자원을 해제합니다.
        /// </summary>
        /// <param name="disposing">관리되는 자원을 해제할지 여부</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
#if DEBUG
                _disposalStack = Environment.StackTrace;
#endif
                if (disposing)
                {
                    var array = Interlocked.Exchange(ref _array, null);
                    if (array != null)
                    {
                        if (!_isTakeOwnership)
                        {
                            ArrayPoolAllocator<T>.Return(array, true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 관리되는 리소스와 비관리되는 리소스를 해제하고 가비지 수집을 억제합니다.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Rented Buffer를 Memory로 변환합니다.
        /// </summary>
        /// <param name="instance"></param>
        public static implicit operator ReadOnlyMemory<T>(RentedBuffer<T> instance) => instance.Memory;
        /// <summary>
        /// 디버깅 도우미
        /// </summary>
        /// <returns></returns>
        public string GetDebugInfo()
        {
            StringBuilder builder = new();
            builder.AppendLine("Buffer Info:");
            builder.AppendLine($"- Capacity: {MaxCapacity}");
            builder.AppendLine($"- Current Length: {_length}");
            builder.AppendLine($"- Current Offset: {_offset}");
            builder.AppendLine($"- Is Disposed: {IsDisposed}");
#if DEBUG
            builder.AppendLine($"- Allocation Stack: {_allocationStack}");
            builder.AppendLine($"- Disposal Stack: {_disposalStack ?? "Not disposed yet"}");
#endif
            return builder.ToString();
        }
    }
}