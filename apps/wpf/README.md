# PathoLog WPF

Windows WPF desktop application for PathoLog. All business logic lives in
`src/shared-dotnet` and this project is a UI shell that consumes those libraries.

## Build and run
1. Open the solution: `apps/wpf/PathoLog.Wpf.sln`.
2. Build or run `PathoLog.Wpf`.

## PDF extraction notes
- PDF text extraction uses PdfPig in `PathoLog.Extraction`.
- OCR uses Tesseract and requires a `tessdata` folder (language data files) at runtime.
