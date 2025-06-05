using ImageReader.Models;
using Polly;
using Polly.Timeout;
using Polly.Wrap;

namespace ImageReader.Infrastructure
{
    public static class PollyPolicies
    {
    
        public static AsyncPolicyWrap<ChatResponseDto> GetChatResiliencePolicy()
        {
            var rateLimit = Policy
                .RateLimitAsync<ChatResponseDto>(
                    15,                       
                    TimeSpan.FromMinutes(1)   
                );

            var retry = Policy<ChatResponseDto>
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, attempt))
                        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100)),
                    onRetry: (outcome, timespan, retryCount, ctx) =>
                        Console.WriteLine(
                            $"[Polly] Retry {retryCount} after {timespan.TotalSeconds:N1}s due to {outcome.Exception.GetType().Name}"
                        )
                );

            var breaker = Policy<ChatResponseDto>
                .Handle<Exception>()
                .AdvancedCircuitBreakerAsync(
                    failureThreshold: 0.5,                     
                    samplingDuration: TimeSpan.FromSeconds(30),
                    minimumThroughput: 5,                    
                    durationOfBreak: TimeSpan.FromSeconds(30), 
                    onBreak: (ex, breakDelay) =>
                        Console.WriteLine(
                            $"[Polly] Circuit broken for {breakDelay.TotalSeconds}s due to {ex.GetType().Name}"
                        ),
                    onReset: () =>
                        Console.WriteLine("[Polly] Circuit reset."),
                    onHalfOpen: () =>
                        Console.WriteLine("[Polly] Circuit half-open: next call is trial.")
                );

            var timeout = Policy
                .TimeoutAsync<ChatResponseDto>(
                    TimeSpan.FromSeconds(30),
                    TimeoutStrategy.Pessimistic,
                    onTimeoutAsync: (ctx, span, task) =>
                    {
                        Console.WriteLine($"[Polly] Execution timed out after {span.TotalSeconds}s");
                        return Task.CompletedTask;
                    }
                );

            return Policy.WrapAsync(rateLimit, retry, breaker, timeout);
        }
    }
}
