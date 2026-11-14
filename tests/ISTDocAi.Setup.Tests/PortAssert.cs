using System.Collections;
using NUnit.Framework;

namespace ISTDocAi.Setup.Tests;

// NUnit constraints with return-valued helpers for mechanically migrated assertions.
internal static class PortAssert
{
    internal static void Fail(string message) => Assert.Fail(message);
    internal static void NotEmpty(IEnumerable value) => Assert.That(value, Is.Not.Empty);
    internal static void NotNull(object? value) => Assert.That(value, Is.Not.Null);
    internal static void Matches(string pattern, string value) => Assert.That(value, Does.Match(pattern));
    internal static void Equal(object? expected, object? actual) => Assert.That(actual, Is.EqualTo(expected));
    internal static void Equal(double expected, double actual, int precision) => Assert.That(Math.Round(actual, precision), Is.EqualTo(Math.Round(expected, precision)));
    internal static void Equal(decimal expected, decimal actual, int precision) => Assert.That(Math.Round(actual, precision), Is.EqualTo(Math.Round(expected, precision)));
    internal static void NotEqual(object? expected, object? actual) => Assert.That(actual, Is.Not.EqualTo(expected));
    internal static void True(bool value) => Assert.That(value, Is.True);
    internal static void False(bool value) => Assert.That(value, Is.False);
    internal static void Null(object? value) => Assert.That(value, Is.Null);
    internal static void Empty(IEnumerable value) => Assert.That(value, Is.Empty);
    internal static void Same(object? expected, object? actual) => Assert.That(actual, Is.SameAs(expected));
    internal static void NotSame(object? expected, object? actual) => Assert.That(actual, Is.Not.SameAs(expected));
    internal static T Single<T>(IEnumerable<T> values)
    {
        var items = values.ToArray();
        Assert.That(items, Has.Length.EqualTo(1));
        return items[0];
    }
    internal static void Single(IEnumerable values) => Assert.That(values.Cast<object>().ToArray(), Has.Length.EqualTo(1));
    internal static T Single<T>(IEnumerable<T> values, Predicate<T> predicate) => Single(values.Where(v => predicate(v)));
    internal static T IsType<T>(object? value)
    {
        Assert.That(value, Is.TypeOf<T>());
        return (T)value!;
    }
    internal static void All<T>(IEnumerable<T> values, Action<T> assertion)
    {
        foreach (var value in values) assertion(value);
    }
    internal static void Contains<T>(T expected, IEnumerable<T> values) => Assert.That(values, Does.Contain(expected));
    internal static void Contains(string expected, string value) => Assert.That(value, Does.Contain(expected));
    internal static void Contains<T>(IEnumerable<T> values, Predicate<T> predicate) => Assert.That(values.Any(v => predicate(v)), Is.True);
    internal static void DoesNotContain<T>(T expected, IEnumerable<T> values) => Assert.That(values, Does.Not.Contain(expected));
    internal static void DoesNotContain<T>(IEnumerable<T> values, Predicate<T> predicate) => Assert.That(values.Any(v => predicate(v)), Is.False);
    internal static void DoesNotContain(string expected, string value, StringComparison comparison = StringComparison.Ordinal) => Assert.That(value.Contains(expected, comparison), Is.False);
    internal static void StartsWith(string expected, string value) => Assert.That(value, Does.StartWith(expected));
    internal static void InRange<T>(T value, T low, T high) => Assert.That(value, Is.InRange(low, high));
    internal static T Throws<T>(TestDelegate action) where T : Exception => Assert.Throws<T>(action)!;
    internal static T Throws<T>(Func<object?> action) where T : Exception => Assert.Throws<T>(() => { _ = action(); })!;
    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        Exception? failure = null;
        try { await action(); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.TypeOf<T>());
        return (T)failure!;
    }

    internal static void True(bool value, string message) => Assert.That(value, Is.True, message);
    internal static void False(bool value, string message) => Assert.That(value, Is.False, message);
    internal static T ThrowsAny<T>(TestDelegate action) where T : Exception => Assert.Catch<T>(action)!;
    internal static async Task<T> ThrowsAnyAsync<T>(Func<Task> action) where T : Exception
    {
        Exception? failure = null;
        try { await action(); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.InstanceOf<T>());
        return (T)failure!;
    }
    internal static async Task<Exception> ThrowsAsync(Type type, Func<Task> action)
    {
        Exception? failure = null;
        try { await action(); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.TypeOf(type));
        return failure!;
    }
    internal static void EndsWith(string expected, string value) => Assert.That(value, Does.EndWith(expected));
    internal static void DoesNotMatch(System.Text.RegularExpressions.Regex regex, string value) => Assert.That(regex.IsMatch(value), Is.False);
}

