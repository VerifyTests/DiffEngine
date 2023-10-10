
## Non-MDI tools

Non-MDI tools are preferred since it allows [DiffEngineTray](tray.md) to track and close diffs.


### [BeyondCompare](https://www.scootersoftware.com)

  * Cost: Paid
  * Is MDI: False
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_BeyondCompare`
  * Supported binaries: pdf, bmp, gif, ico, jpg, jpeg, png, tif, tiff, rtf

#### Notes:

 * [Command line reference](https://www.scootersoftware.com/v4help/index.html?command_line_reference.html)
 * Enable [Automatically reload unless changes will be discarded](https://www.scootersoftware.com/v4help/optionstweak.html) in `Tools > Options > Tweaks > File Operations`. 

#### Windows settings:

  * Example target on left arguments: `/solo /rightreadonly "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `/solo /leftreadonly "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\Beyond Compare *\BCompare.exe`
    * `%ProgramW6432%\Beyond Compare *\BCompare.exe`
    * `%ProgramFiles(x86)%\Beyond Compare *\BCompare.exe`
    * `%PATH%BCompare.exe`

#### OSX settings:

  * Example target on left arguments: `-solo -rightreadonly "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `-solo -leftreadonly "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/Applications/Beyond Compare.app/Contents/MacOS/bcomp`
    * `%PATH%bcomp`

#### Linux settings:

  * Example target on left arguments: `-solo -rightreadonly "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `-solo -leftreadonly "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/usr/lib/beyondcompare/bcomp`
    * `%PATH%bcomp`

### [DeltaWalker](https://www.deltawalker.com/)

  * Cost: Paid
  * Is MDI: False
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_DeltaWalker`
  * Supported binaries: jpg, jp2, j2k, png, gif, psd, tif, bmp, pct, pict, pic, ico, ppm, pgm, pbm, pnm, zip, jar, ear, tar, tgz, tbz2, gz, bz2, doc, docx, xls, xlsx, ppt, pdf, rtf, html, htm

#### Notes:

 * [Command line usage](https://www.deltawalker.com/integrate/command-line)

#### Windows settings:

  * Example target on left arguments: `-mi "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `-mi "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `C:\Program Files\Deltopia\DeltaWalker\DeltaWalker.exe`
    * `%PATH%DeltaWalker.exe`

#### OSX settings:

  * Example target on left arguments: `-mi "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `-mi "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/Applications/DeltaWalker.app/Contents/MacOS/DeltaWalker`
    * `%PATH%DeltaWalker`

### [Diffinity](https://truehumandesign.se/s_diffinity.php)

  * Cost: Free with option to donate
  * Is MDI: False
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_Diffinity`

#### Notes:

 * Disable single instance:
   \ Preferences \ Tabs \ uncheck `Use single instance and open new diffs in tabs`.

#### Windows settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\Diffinity\Diffinity.exe`
    * `%ProgramW6432%\Diffinity\Diffinity.exe`
    * `%ProgramFiles(x86)%\Diffinity\Diffinity.exe`
    * `%PATH%Diffinity.exe`

### [ExamDiff](https://www.prestosoft.com/edp_examdiffpro.asp)

  * Cost: Paid
  * Is MDI: False
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_ExamDiff`

#### Notes:

 * [Command line reference](https://www.prestosoft.com/ps.asp?page=htmlhelp/edp/command_line_options)
 * `/nh`: do not add files or directories to comparison history
 * `/diffonly`: diff-only merge mode: hide the merge pane

#### Windows settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" /nh /diffonly /dn1:targetFile.txt /dn2:tempFile.txt `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" /nh /diffonly /dn1:tempFile.txt /dn2:targetFile.txt `
  * Scanned paths:
    * `%ProgramFiles%\ExamDiff Pro\ExamDiff.exe`
    * `%ProgramW6432%\ExamDiff Pro\ExamDiff.exe`
    * `%ProgramFiles(x86)%\ExamDiff Pro\ExamDiff.exe`
    * `%PATH%ExamDiff.exe`

### [Guiffy](https://www.guiffy.com/)

  * Cost: Paid
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_Guiffy`
  * Supported binaries: bmp, gif, jpeg, jpg, png, wbmp

#### Notes:

 * [Command line reference](https://www.guiffy.com/help/GuiffyHelp/GuiffyCmd.html)
 * [Image Diff Tool](https://www.guiffy.com/Image-Diff-Tool.html)
 * `-ge1`: Forbid first file view Editing
 * `-ge2`: Forbid second file view Editing

#### Windows settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" -ge2 `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" -ge1 `
  * Scanned paths:
    * `%ProgramFiles%\Guiffy\guiffy.exe`
    * `%ProgramW6432%\Guiffy\guiffy.exe`
    * `%ProgramFiles(x86)%\Guiffy\guiffy.exe`
    * `%PATH%guiffy.exe`

#### OSX settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" -ge2 `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" -ge1 `
  * Scanned paths:
    * `/Applications/Guiffy/guiffyCL.command`
    * `%PATH%guiffyCL.command`

### [Kaleidoscope](https://kaleidoscope.app)

  * Cost: Paid
  * Is MDI: False
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_Kaleidoscope`
  * Supported binaries: bmp, gif, ico, jpg, jpeg, png, tiff, tif

#### OSX settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%PATH%ksdiff`

### [KDiff3](https://github.com/KDE/kdiff3)

  * Cost: Free
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_KDiff3`

#### Notes:

 * `--cs CreateBakFiles=0` to not save a `.orig` file when merging

#### Windows settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" --cs CreateBakFiles=0 `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" --cs CreateBakFiles=0 `
  * Scanned paths:
    * `%ProgramFiles%\KDiff3\kdiff3.exe`
    * `%ProgramW6432%\KDiff3\kdiff3.exe`
    * `%ProgramFiles(x86)%\KDiff3\kdiff3.exe`
    * `%PATH%kdiff3.exe`

#### OSX settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" --cs CreateBakFiles=0 `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" --cs CreateBakFiles=0 `
  * Scanned paths:
    * `/Applications/kdiff3.app/Contents/MacOS/kdiff3`
    * `%PATH%kdiff3`

### [Neovim](https://neovim.io/)

  * Cost: Free with option to sponsor
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_Neovim`

#### Notes:

 * https://neovim.io/doc/user/diff.html

#### Windows settings:

  * Example target on left arguments: `-d "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `-d "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%PATH%nvim.exe`

#### OSX settings:

  * Example target on left arguments: `-d "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `-d "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%PATH%nvim`

#### Linux settings:

  * Example target on left arguments: `-d "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `-d "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%PATH%nvim`

### [P4Merge](https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge)

  * Cost: Free
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_P4Merge`
  * Supported binaries: bmp, gif, jpg, jpeg, png, pbm, pgm, ppm, tif, tiff, xbm, xpm

#### Windows settings:

  * Example target on left arguments for text: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments for text: `"tempFile.txt" "targetFile.txt" `
  * Example target on left arguments for binary: `-C utf8-bom "tempFile.png" "targetFile.png" "targetFile.png" "targetFile.png" `
  * Example target on right arguments for binary: `-C utf8-bom "targetFile.png" "tempFile.png" "targetFile.png" "targetFile.png" `
  * Scanned paths:
    * `%ProgramFiles%\Perforce\p4merge.exe`
    * `%ProgramW6432%\Perforce\p4merge.exe`
    * `%ProgramFiles(x86)%\Perforce\p4merge.exe`
    * `%PATH%p4merge.exe`

#### OSX settings:

  * Example target on left arguments for text: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments for text: `"tempFile.txt" "targetFile.txt" `
  * Example target on left arguments for binary: `-C utf8-bom "tempFile.png" "targetFile.png" "targetFile.png" "targetFile.png" `
  * Example target on right arguments for binary: `-C utf8-bom "targetFile.png" "tempFile.png" "targetFile.png" "targetFile.png" `
  * Scanned paths:
    * `/Applications/p4merge.app/Contents/MacOS/p4merge`
    * `%PATH%p4merge`

#### Linux settings:

  * Example target on left arguments for text: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments for text: `"tempFile.txt" "targetFile.txt" `
  * Example target on left arguments for binary: `-C utf8-bom "tempFile.png" "targetFile.png" "targetFile.png" "targetFile.png" `
  * Example target on right arguments for binary: `-C utf8-bom "targetFile.png" "tempFile.png" "targetFile.png" "targetFile.png" `
  * Scanned paths:
    * `%PATH%p4merge`

### [Rider](https://www.jetbrains.com/rider/)

  * Cost: Paid with free option for OSS
  * Is MDI: False
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_Rider`

#### Notes:

 * https://www.jetbrains.com/help/rider/Command_Line_Differences_Viewer.html

#### Windows settings:

  * Example target on left arguments: `diff "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `diff "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%LOCALAPPDATA%\Programs\Rider\bin\rider64.exe`
    * `%PATH%rider.cmd`

#### OSX settings:

  * Example target on left arguments: `diff "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `diff "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/Applications/Rider.app/Contents/MacOS/rider`
    * `%PATH%rider`

#### Linux settings:

  * Example target on left arguments: `diff "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `diff "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%HOME%/.local/share/JetBrains/Toolbox/apps/Rider/*/*/bin/rider.sh`
    * `%PATH%rider.sh`

### [TkDiff](https://sourceforge.net/projects/tkdiff/)

  * Cost: Free
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_TkDiff`

#### OSX settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/Applications/TkDiff.app/Contents/MacOS/tkdiff`
    * `%PATH%tkdiff`

### [TortoiseGitIDiff](https://tortoisegit.org/docs/tortoisegitmerge/)

  * Cost: Free
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: False
  * Environment variable for custom install location: `DiffEngine_TortoiseGitIDiff`
  * Supported binaries: bmp, gif, ico, jpg, jpeg, png, tif, tiff

#### Windows settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\TortoiseGit\bin\TortoiseGitIDiff.exe`
    * `%ProgramW6432%\TortoiseGit\bin\TortoiseGitIDiff.exe`
    * `%ProgramFiles(x86)%\TortoiseGit\bin\TortoiseGitIDiff.exe`
    * `%PATH%TortoiseGitIDiff.exe`

### [TortoiseGitMerge](https://tortoisegit.org/docs/tortoisegitmerge/)

  * Cost: Free
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_TortoiseGitMerge`

#### Windows settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\TortoiseGit\bin\TortoiseGitMerge.exe`
    * `%ProgramW6432%\TortoiseGit\bin\TortoiseGitMerge.exe`
    * `%ProgramFiles(x86)%\TortoiseGit\bin\TortoiseGitMerge.exe`
    * `%PATH%TortoiseGitMerge.exe`

### [TortoiseIDiff](https://tortoisesvn.net/TortoiseIDiff.html)

  * Cost: Free
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: False
  * Environment variable for custom install location: `DiffEngine_TortoiseIDiff`
  * Supported binaries: bmp, gif, ico, jpg, jpeg, png, tif, tiff

#### Windows settings:

  * Example target on left arguments: `/left:"targetFile.txt" /right:"tempFile.txt" `
  * Example target on right arguments: `/left:"tempFile.txt" /right:"targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\TortoiseSVN\bin\TortoiseIDiff.exe`
    * `%ProgramW6432%\TortoiseSVN\bin\TortoiseIDiff.exe`
    * `%ProgramFiles(x86)%\TortoiseSVN\bin\TortoiseIDiff.exe`
    * `%PATH%TortoiseIDiff.exe`

### [TortoiseMerge](https://tortoisesvn.net/TortoiseMerge.html)

  * Cost: Free
  * Is MDI: False
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_TortoiseMerge`

#### Windows settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\TortoiseSVN\bin\TortoiseMerge.exe`
    * `%ProgramW6432%\TortoiseSVN\bin\TortoiseMerge.exe`
    * `%ProgramFiles(x86)%\TortoiseSVN\bin\TortoiseMerge.exe`
    * `%PATH%TortoiseMerge.exe`

### [Vim](https://www.vim.org/)

  * Cost: Free with option to donate
  * Is MDI: False
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_Vim`

#### Notes:

 * [Options](http://vimdoc.sourceforge.net/htmldoc/options.html)
 * [Vim help files](https://vimhelp.org/)
 * [autoread](http://vimdoc.sourceforge.net/htmldoc/options.html#'autoread')
 * [nobackup](http://vimdoc.sourceforge.net/htmldoc/options.html#'backup')
 * [noswapfile](http://vimdoc.sourceforge.net/htmldoc/options.html#'swapfile')

#### Windows settings:

  * Example target on left arguments: `-d "targetFile.txt" "tempFile.txt" -c "setl autoread | setl nobackup | set noswapfile" `
  * Example target on right arguments: `-d "tempFile.txt" "targetFile.txt" -c "setl autoread | setl nobackup | set noswapfile" `
  * Scanned paths:
    * `%ProgramFiles%\Vim\*\vim.exe`
    * `%ProgramW6432%\Vim\*\vim.exe`
    * `%ProgramFiles(x86)%\Vim\*\vim.exe`
    * `%PATH%vim.exe`

#### OSX settings:

  * Example target on left arguments: `-d "targetFile.txt" "tempFile.txt" -c "setl autoread | setl nobackup | set noswapfile" `
  * Example target on right arguments: `-d "tempFile.txt" "targetFile.txt" -c "setl autoread | setl nobackup | set noswapfile" `
  * Scanned paths:
    * `/Applications/MacVim.app/Contents/bin/mvim`
    * `%PATH%mvim`

### [WinMerge](https://winmerge.org/)

  * Cost: Free with option to donate
  * Is MDI: False
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_WinMerge`
  * Supported binaries: bmp, cut, dds, exr, g3, gif, hdr, ico, iff, lbm, j2k, j2c, jng, jp2, jpg, jif, jpeg, jpe, jxr, wdp, hdp, koa, mng, pcd, pcx, pfm, pct, pict, pic, png, pbm, pgm, ppm, psd, ras, sgi, rgb, rgba, bw, tga, targa, tif, tiff, wap, wbmp, wbm, webp, xbm, xpm

#### Notes:

 * [Command line reference](https://manual.winmerge.org/en/Command_line.html).
 * `/u` Prevents WinMerge from adding paths to the Most Recently Used (MRU) list.
 * `/wl` Opens the left side as read-only.
 * `/dl` and `/dr` Specifies file descriptions in the title bar.
 * `/e` Enables close with a single Esc key press.
 * `/cfg Backup/EnableFile=0` disable backup files.

#### Windows settings:

  * Example target on left arguments: `/u /wr /e "targetFile.txt" "tempFile.txt" /dl "targetFile.txt" /dr "tempFile.txt" /cfg Backup/EnableFile=0 `
  * Example target on right arguments: `/u /wl /e "tempFile.txt" "targetFile.txt" /dl "tempFile.txt" /dr "targetFile.txt" /cfg Backup/EnableFile=0 `
  * Scanned paths:
    * `%ProgramFiles%\WinMerge\WinMergeU.exe`
    * `%ProgramW6432%\WinMerge\WinMergeU.exe`
    * `%ProgramFiles(x86)%\WinMerge\WinMergeU.exe`
    * `%LocalAppData%\Programs\WinMerge\WinMergeU.exe`
    * `%PATH%WinMergeU.exe`

## MDI tools


### [AraxisMerge](https://www.araxis.com/merge)

  * Cost: Paid
  * Is MDI: True
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_AraxisMerge`
  * Supported binaries: bmp, dib, emf, gif, jif, j2c, j2k, jp2, jpc, jpeg, jpg, jpx, pbm, pcx, pgm, png, ppm, ras, tif, tiff, tga, wmf

#### Notes:

 * [Supported image files](https://www.araxis.com/merge/documentation-windows/comparing-image-files.en)
 * [Windows command line usage](https://www.araxis.com/merge/documentation-windows/command-line.en)
 * [MacOS command line usage](https://www.araxis.com/merge/documentation-os-x/command-line.en)
 * [Installing MacOS command line](https://www.araxis.com/merge/documentation-os-x/installing.en)

#### Windows settings:

  * Example target on left arguments: `/nowait "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `/nowait "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\Araxis\Araxis Merge\Compare.exe`
    * `%ProgramW6432%\Araxis\Araxis Merge\Compare.exe`
    * `%ProgramFiles(x86)%\Araxis\Araxis Merge\Compare.exe`
    * `%PATH%Compare.exe`

#### OSX settings:

  * Example target on left arguments: `-nowait "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `-nowait "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/Applications/Araxis Merge.app/Contents/Utilities/compare`
    * `%PATH%compare`

### [Meld](https://meldmerge.org/)

  * Cost: Free
  * Is MDI: True
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_Meld`

#### Notes:

 * While Meld is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Meld merge process for a specific diff. [Vote for this feature](https://gitlab.gnome.org/GNOME/meld/-/issues/584)

#### Windows settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%LOCALAPPDATA%\Programs\Meld\meld.exe`
    * `%ProgramFiles%\Meld\meld.exe`
    * `%ProgramW6432%\Meld\meld.exe`
    * `%ProgramFiles(x86)%\Meld\meld.exe`
    * `%PATH%meld.exe`

#### OSX settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/Applications/meld.app/Contents/MacOS/meld`
    * `%PATH%meld`

#### Linux settings:

  * Example target on left arguments: `"targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `"tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%PATH%meld`

### [SublimeMerge](https://www.sublimemerge.com/)

  * Cost: Paid
  * Is MDI: True
  * Supports auto-refresh: False
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_SublimeMerge`

#### Notes:

 * While SublimeMerge is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Sublime merge process for a specific diff. [Vote for this feature](https://github.com/sublimehq/sublime_merge/issues/1168)

#### Windows settings:

  * Example target on left arguments: `mergetool "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `mergetool "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\Sublime Merge\smerge.exe`
    * `%ProgramW6432%\Sublime Merge\smerge.exe`
    * `%ProgramFiles(x86)%\Sublime Merge\smerge.exe`
    * `%PATH%smerge.exe`

#### OSX settings:

  * Example target on left arguments: `mergetool "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `mergetool "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/Applications/smerge.app/Contents/MacOS/smerge`
    * `%PATH%smerge`

#### Linux settings:

  * Example target on left arguments: `mergetool "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `mergetool "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%PATH%smerge`

### [VisualStudio](https://docs.microsoft.com/en-us/visualstudio/ide/reference/diff)

  * Cost: Paid and free options
  * Is MDI: True
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_VisualStudio`

#### Windows settings:

  * Example target on left arguments: `/diff "targetFile.txt" "tempFile.txt" "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `/diff "tempFile.txt" "targetFile.txt" "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%ProgramFiles%\Microsoft Visual Studio\2022\Preview\Common7\IDE\devenv.exe`
    * `%ProgramW6432%\Microsoft Visual Studio\2022\Preview\Common7\IDE\devenv.exe`
    * `%ProgramFiles(x86)%\Microsoft Visual Studio\2022\Preview\Common7\IDE\devenv.exe`
    * `%ProgramFiles%\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe`
    * `%ProgramW6432%\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe`
    * `%ProgramFiles(x86)%\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe`
    * `%ProgramFiles%\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe`
    * `%ProgramW6432%\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe`
    * `%ProgramFiles(x86)%\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe`
    * `%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe`
    * `%ProgramW6432%\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe`
    * `%ProgramFiles(x86)%\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe`
    * `%PATH%devenv.exe`

### [VisualStudioCode](https://code.visualstudio.com)

  * Cost: Free
  * Is MDI: True
  * Supports auto-refresh: True
  * Supports text files: True
  * Environment variable for custom install location: `DiffEngine_VisualStudioCode`

#### Notes:

 * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)

#### Windows settings:

  * Example target on left arguments: `--diff "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `--diff "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%LocalAppData%\Programs\Microsoft VS Code\code.exe`
    * `%ProgramFiles%\Microsoft VS Code\code.exe`
    * `%ProgramW6432%\Microsoft VS Code\code.exe`
    * `%ProgramFiles(x86)%\Microsoft VS Code\code.exe`
    * `%PATH%code.exe`

#### OSX settings:

  * Example target on left arguments: `--diff "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `--diff "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code`
    * `%PATH%code`

#### Linux settings:

  * Example target on left arguments: `--diff "targetFile.txt" "tempFile.txt" `
  * Example target on right arguments: `--diff "tempFile.txt" "targetFile.txt" `
  * Scanned paths:
    * `%PATH%code`
