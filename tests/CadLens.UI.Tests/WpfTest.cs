using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Serializes WPF checks that share app language and appearance resources.</summary>
[CollectionDefinition("WPF", DisableParallelization = true)]
public sealed class WpfCollection;

internal static class WpfTest
{
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;

            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    internal static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                try
                {
                    Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF check did not finish.");
        Assert.Null(failure);
    }
}
