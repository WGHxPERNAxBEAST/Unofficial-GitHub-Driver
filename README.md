# Unofficial-GitHub-Driver
This project is The Open Source (and Un-Official) GitHub Copilot Manager Agent/Extension capable of managing Repositories automatically using Copilot & GitHub APIs.

This will be a GitHub Copilot Agent and/or Visual Studio Extension written in C# using .NET 10. It handles accessing GitHub's APIs and performing/managing the high level tasks being assigned by the User which we break down into smaller tasks for Copilot to complete. Copilot may be used to break the task down to reasonable level. 

User should be able to assign a coding task to fully complete in a specific repository (meaning code is fully and thoroughly reviewed and tested for 100% correctness before finally being committing to end branch (prior to test and code review all work should be done in a new branch (or new branches from that one that end up merged back in))) When work is fully complete in its development branch and the code has been tested/reviewed then accepted, it should be squashed and merged into the branch the change was requested on. Each task from the user should be 1 commit to the requested branch in the end and it must always be perfect! 
