# ExportacaoLoteDWG

Plugin para **AutoCAD 2021** que automatiza o comando **EXPORTLAYOUT** (*Export Layout to Model*) em vários arquivos DWG de uma só vez.

Pensado para o fluxo de folhas exportadas do **Revit**: em vez de abrir cada DWG, executar o comando no Layout1 e salvar o resultado manualmente, o plugin faz isso em lote.

## O que faz

1. Você digita o comando `EXPORTLOTE` no AutoCAD.
2. Seleciona os DWGs desejados (seleção múltipla).
3. Escolhe a pasta de destino.
4. O plugin abre cada DWG, vai para o **Layout1** (ou o primeiro layout de papel, se não existir), executa o `EXPORTLAYOUT` e salva o novo DWG na pasta escolhida com o mesmo nome do original.

Ao final, exibe no AutoCAD o total de arquivos exportados e de falhas, e grava um `ExportacaoLote_log.txt` na pasta de destino.

## Requisitos

- AutoCAD 2021
- .NET Framework 4.8
- Visual Studio (para compilar)

## Compilação

1. Crie/abra um projeto **Biblioteca de Classes (.NET Framework 4.8)**, plataforma **x64**.
2. Adicione as referências do AutoCAD 2021 (da pasta de instalação ou do ObjectARX SDK):
   - `accoremgd.dll`
   - `acdbmgd.dll`
   - `acmgd.dll`

   Marque **Copy Local = False** nas três.
3. Adicione também a referência `System.Windows.Forms`.
4. Inclua o arquivo `Comandos.cs` e compile.

## Uso

1. No AutoCAD, execute `NETLOAD` e selecione o `ExportacaoLoteDWG.dll`.
2. Digite `EXPORTLOTE`.
3. Selecione os DWGs e a pasta de destino.
4. Aguarde a mensagem de conclusão na linha de comando.

## Comportamento e cuidados

- Os DWGs de origem **não são modificados** (são fechados sem salvar).
- Se já existir um arquivo com o mesmo nome na pasta de destino, ele é **substituído**.
- Se a pasta de destino for a mesma do original, o arquivo é salvo com o sufixo `_Model.dwg` para não sobrescrever o original.
- A variável `FILEDIA` é temporariamente definida como 0 durante a execução (para o `EXPORTLAYOUT` não abrir a janela de salvar) e restaurada ao final.
- Se um arquivo falhar, o erro é registrado no log (com a etapa em que ocorreu) e o processo continua com os demais.

## Observações técnicas

O `EXPORTLAYOUT` não possui API gerenciada, então é chamado via `Editor.CommandAsync`, dentro de `DocumentCollection.ExecuteInCommandContextAsync`, com o comando registrado com `CommandFlags.Session` (necessário para abrir e fechar outros desenhos).

## Aviso

Use por sua conta e risco. Teste primeiro com uma cópia de poucos arquivos antes de rodar em um lote grande.
