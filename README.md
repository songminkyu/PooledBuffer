# PooledBuffer

A high-performance buffer management library leveraging `ArrayPool` to minimize memory fragmentation and improve application performance for network communications and data processing tasks.

## Overview

This project demonstrates efficient memory management techniques for applications that handle large amounts of data or require frequent buffer allocations. By utilizing pooled memory resources through the `ArrayPool` mechanism, this library helps reduce garbage collection pressure and memory fragmentation, which is particularly beneficial for high-throughput applications.

## Key Components

### RentedBuffer<T>

A generic buffer implementation that acquires memory from an `ArrayPool` and automatically returns it when disposed. Key features include:

- Memory pooling to minimize allocations and reduce GC pressure
- Thread-safe operations for concurrent environments
- Support for read and write operations with appropriate boundary checks
- Optimized memory copying with parallel processing for large data sets
- Efficient memory usage through centralized allocation and reclamation

### ArrayPoolAllocator<T>

A static utility class that manages array pools for different buffer sizes:

- Separate pools for small (≤1024) and large (>1024) arrays
- Shared empty array instance for zero-length requests
- Automatic pool selection based on requested size
- Memory clearing options for security-sensitive applications

## Usage Scenarios

This library is particularly well-suited for:

1. **Network Communication**: Efficiently handle network packets and streams with minimal memory overhead
2. **Large Data Processing**: Process substantial datasets with reduced memory fragmentation
3. **High-Throughput Applications**: Maintain performance in scenarios with frequent buffer allocations and deallocations
4. **Resource-Constrained Environments**: Optimize memory usage in environments with limited resources

## Example Usage

The provided sample code demonstrates several practical applications:

1. **Basic Buffer Operations**: Creating, writing to, reading from, and managing buffers
2. **Network Communication**: Implementing a TCP echo server and client using pooled buffers
3. **Performance Comparison**: Benchmarking standard allocation versus pooled buffers
4. **Packet Processing**: Processing structured network packets using efficient buffer management
5. **Message Building**: Constructing and streaming large messages with optimized memory usage

## Performance Benefits

Performance tests demonstrate significant improvements when using pooled buffers:

- Reduced allocation and deallocation overhead
- Decreased memory fragmentation
- Lower GC pressure and fewer collections
- Improved throughput for high-volume operations

## Getting Started

1. Add this library to your project
2. Replace standard array allocations with `RentedBuffer<T>` instances
3. Ensure proper disposal of buffers using `using` statements or explicit `Dispose()` calls
4. For advanced scenarios, utilize the specialized utility classes for packet processing and message building

## Best Practices

1. Always dispose `RentedBuffer<T>` instances when done, preferably with `using` statements
2. Use appropriate buffer sizes to avoid unnecessary over-allocation
3. Consider thread safety requirements when sharing buffers across threads
4. For very large data sets, leverage the parallel processing capabilities of the library

## Requirements

- .NET 6.0 or later
- System.Buffers package (for ArrayPool support)

## License

[MIT License](LICENSE)

## Reference
 - https://gist.github.com/berrzebb
