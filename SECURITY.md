# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately through GitHub
instead: open the **Security** tab of this repository and choose **Report a vulnerability**.

Helpful details:

- the Shutdown version and your Windows version
- what an attacker could do and the steps to reproduce it

You get an answer within a few days. Once a fix is released, the report is credited in the
changelog unless you prefer to stay anonymous.

## Scope

Shutdown makes no network connections. It starts the Windows `shutdown.exe` with the chosen
arguments and keeps its settings in `%APPDATA%\Shutdown\`. Reports about how it builds those
arguments or handles its settings file are in scope.
