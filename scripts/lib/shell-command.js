"use strict";

const childProcess = require("child_process");

/**
 * Convert a command name to the platform-specific executable name.
 * npm/npx on Windows are exposed as .cmd shims.
 *
 * This helper only adjusts the command token. Use spawnPlatformCommandSync()
 * for child_process execution so Windows shell requirements are applied.
 * @deprecated For child_process calls, use spawnPlatformCommandSync().
 *
 * @param {string} command - Base command name (for example "npm")
 * @param {string} platform - Process platform string
 * @returns {string} Command adjusted for platform execution
 */
function toShellCommand(command, platform = process.platform) {
    return platform === "win32" ? `${command}.cmd` : command;
}

/**
 * Determine whether a command uses Windows shell shims.
 *
 * @param {string} command - Base command name
 * @returns {boolean} True when command is npm/npx
 */
function isShellShimCommand(command) {
    return command === "npm" || command === "npx";
}

/**
 * Resolve a platform-aware command token for spawn-style execution.
 *
 * @param {string} command - Base command name
 * @param {string} platform - Process platform string
 * @returns {string} Resolved command name
 */
function resolveSpawnCommand(command, platform = process.platform) {
    if (platform === "win32" && isShellShimCommand(command)) {
        return toShellCommand(command, platform);
    }

    return command;
}

/**
 * Resolve platform-aware spawn options.
 *
 * Windows npm/npx shims are batch files and must run with shell enabled.
 * Keep this logic centralized so callers cannot forget it.
 *
 * @param {string} command - Base command name
 * @param {object} options - Existing spawn options
 * @param {string} platform - Process platform string
 * @returns {object} Resolved spawn options
 */
function resolveSpawnOptions(command, options = {}, platform = process.platform) {
    const resolvedOptions = { ...options };

    if (platform === "win32" && isShellShimCommand(command)) {
        resolvedOptions.shell = true;

        if (resolvedOptions.windowsHide === undefined) {
            resolvedOptions.windowsHide = true;
        }
    }

    return resolvedOptions;
}

/**
 * Spawn a platform-aware child process.
 *
 * @param {string} command - Base command name
 * @param {string[]} args - Command arguments
 * @param {object} options - spawnSync options
 * @param {Function} spawnSyncImpl - Optional spawnSync implementation for tests
 * @param {string} platform - Process platform string
 * @returns {object} spawnSync result object
 */
function spawnPlatformCommandSync(
    command,
    args = [],
    options = {},
    spawnSyncImpl = childProcess.spawnSync,
    platform = process.platform
) {
    const resolvedCommand = resolveSpawnCommand(command, platform);
    const resolvedOptions = resolveSpawnOptions(command, options, platform);

    return spawnSyncImpl(resolvedCommand, args, resolvedOptions);
}

module.exports = {
    toShellCommand,
    isShellShimCommand,
    resolveSpawnCommand,
    resolveSpawnOptions,
    spawnPlatformCommandSync,
};
