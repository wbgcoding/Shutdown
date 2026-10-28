# Contributing

## Before you start

- **Bugs and ideas:** open an issue with the matching template first, so we can agree on the
  approach before you spend time on code.
- **Security problems:** never in a public issue, see [SECURITY.md](SECURITY.md).

## Build and test

You need Windows 10 or 11 and the .NET 8 SDK or newer (it builds the .NET Framework 4.8 app
through reference assemblies).

```bat
dotnet test tests/Shutdown.Tests
build.bat
```

`build.bat` builds the release exe into `dist\`.

## Guidelines

- **Small and portable.** No installer, no extra runtime, no network access. Settings stay in
  `%APPDATA%\Shutdown\`, never beside the exe.
- **Tests with every change.** New behaviour comes with a test; a bug fix comes with a test that
  failed before the fix.
- **Both languages.** Every user-facing text goes into `Lang/en.resx` and `Lang/de.resx`.
- **Automation ids.** Every interactive control gets an `AutomationId`.
- **One topic per pull request,** with a short description of what changed and how you tested it.

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
