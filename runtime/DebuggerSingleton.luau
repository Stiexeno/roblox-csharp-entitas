-- Tiny shared slot for the active Debugger instance. Lives here (and not on
-- Debugger.luau) so Feature can lazy-poll Current without pulling the whole
-- plasma + widgets graph into every project's bootstrap. Debugger.Run sets
-- Current; Feature.new reads it; that's the entire surface.

local DebuggerSingleton = {}
DebuggerSingleton.Current = nil

return DebuggerSingleton
