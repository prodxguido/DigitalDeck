using Xunit;

// WPF is single-threaded per process: one Application, one MediaContext, thread affinity on every
// visual. Tests here that spin up a window (through WpfThread) and tests that render offscreen
// (RenderTargetBitmap in IconNormalizer, the icon sheet) cannot run at the same time — whichever
// touches the graphics stack second gets a blank frame back. Collections run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
