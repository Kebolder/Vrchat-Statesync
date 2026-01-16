# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 08-14-2025
### Added
- First upload
---
## [1.0.1] - 08-15-2025
### Fixed
- Fixed binary mode account for ALL states even if theyre not used
---
## [1.1.0] - 12-18-2025
### Changed
- Recoded the entire tool, this is a completely new version that supports new features below:
### Added
- Copy transition time from local (Cloned states will now copy their respective transition time from their local states)
---
## [1.1.1] - 12-19-2025
### Changed
- Changed the State section to use buttons to focus on the state you select
### Added
- Clear state(s) by prefixs
---
## [1.1.2] - 12-19-2025
### Fixed
- Font colors in the state info box being black.
---
## [1.1.3] - 12-19-2025
### Added
- Error pop up when conflicitng states exist.
- Clone aniamtor setting
- Basic how-to guide in documents

### Fixed
- Conflict showing on wrong state
---
## [1.1.4] - 12-19-2015
### Fixed
- When cloning a layer with a bunch of states will lose its dictionary thus causing the states to be dropped.

---
## [1.1.5]
#### Fixed
- unitypackage tar
---
## [1.2.0]
### Added
- Added Binary sync mode.
---
## [1.2.1]
### Changed
- Changed StateMachine -> Sub-State Machine in the menu.
---
## [1.2.2] - 1/11/2026
### Added
- Under utilities added the ability to add a parameter list which will make sure it has your remote synced parameter inside it incase you forgor 
---
## [1.2.3] - 1/16/2026
### Fixed
- "Remove parameter from remote" would alos remove parameters from the local side before cloning woops!
---
## [1.2.4] - 1/16/2026
### Fixed
- Fixed remove again not removing drivers on the remote 
---