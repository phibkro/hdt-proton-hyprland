// Steam's overlay belongs to the game. Its X11 hooks can deadlock HDT's
// auxiliary Wine process while that process polls Windows mouse state.
export function trackerEnvironment(
  inherited: Record<string, string>
): Record<string, string> {
  const result = { ...inherited };

  if (inherited.LD_PRELOAD !== undefined) {
    // The Linux loader treats both colons and whitespace as separators.
    const libraries = inherited.LD_PRELOAD.split(/[:\s]+/)
      .filter(Boolean)
      .filter((path) => path.split("/").at(-1) !== "gameoverlayrenderer.so");

    if (libraries.length) {
      result.LD_PRELOAD = libraries.join(":");
    } else {
      delete result.LD_PRELOAD;
    }
  }

  return result;
}
