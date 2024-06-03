# PhiPdfRag
This is Local RAG Chat to Pdf File using SLM Model: Phi 3

Requirements
- Visual Studio 2022
- .NET 8 or Higher

Downloading Models
You also need to download and extract the two models to the correct folders.

All models should be put in a folder that you need to create called "onnx-models", inside the project folder (PDFAnalyzer).

The models can be downloaded from the following links:

https://huggingface.co/optimum/all-MiniLM-L6-v2
https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx
Clone the repositories and extract the models to the right folders.

Phi-3-mini-4k-instruct-onnx has 3 different versions inside it's repo. We are using the DirectML versions in this project. Create a folder called "phi3-directml-int4-awq-block-128" inside the "onnx-models" folder and copy the contents of the "directml/directml-int4-awq-block-128" folder to this new folder you created.

You don't need to modify the project, as it is already including all the files in the onnx-models folder to the output directory.

The final folder structure should look like this:
```
PDFAnalyzer
│   onnx-models
│   ├── all-MiniLM-L6-v2
│   │   ├── model.onnx
│   │   ├── vocab.txt
│   ├── phi3-directml-int4-awq-block-128
│   │   ├── added_tokens.json
│   │   ├── genai_config.json
│   │   ├── model.onnx
│   │   ├── model.onnx.data
│   │   ├── special_tokens_map.json
│   │   ├── tokenizer_config.json
│   │   ├── tokenizer.json
│   │   ├── tokenizer.model
```
