# Code versus machine level settings

The approach to when a config setting is either a code based API or a machine level environment variable is driven by if the setting is a personal preference or shared.


## Personal preferences

Settings that are personal preferences ideally should

 * Not be share with other people using the same code base.
 * When running on the same machine, operate the same for different code bases that all use DiffEngine.
 * Not apply to CI.

Examples:

 * [Custom order](diff-tool.order.md#custom-order) which depend on the tools a specific person has installed.
 * [Disable launching](/#disable-for-a-machineprocess).
 * [Max instances to launch](diff-tool.md#maxinstancestolaunch).


## Shared preferences

Settings that are shared ideally should

 * Checked into the code base.
 * Be share with other people using the same code base.
 * When running on the same machine, not effect different code bases that all use DiffEngine.
 * Apply to CI.

Examples:

 * [File type detection](/#file-type-detection).
 * [Adding a share custom tool](/diff-tool.custom.md). A team may be comparing custom file types, and require a shared custom tool to verify that file.