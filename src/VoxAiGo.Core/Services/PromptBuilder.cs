using System.Text;
using VoxAiGo.Core.Models;

namespace VoxAiGo.Core.Services;

public static class PromptBuilder
{
    private const string SpeechCleanupRules = """
        SPEECH CLEANUP (CRITICAL):
        Remove all speech disfluencies and verbal artifacts:
        - Filler sounds: "uh", "um", "ah", "er", "hmm", "hm", "huh", "eh"
        - Portuguese fillers: "é...", "então", "tipo", "né", "assim", "bem", "ahn", "éé"
        - Verbal pauses: "so...", "well...", "like...", "you know..."
        - False starts: "I want to- I need to" → keep only "I need to"
        - Repetitions: "the the" → "the", "I I think" → "I think"
        - Stutters: "c-can you" → "can you"
        - Breath sounds and lip smacks

        SELF-CORRECTION HANDLING:
        When the user corrects themselves, use ONLY the correction:
        - "X, no wait, Y" → Y
        - "X, I mean Y" → Y
        - "X, actually Y" → Y
        - "X, sorry, Y" → Y
        - "não, espera" / "quer dizer" / "na verdade" / "desculpa" (Portuguese)
        Example: "create function foo, no wait, bar" → function named "bar"

        Output ONLY the clean, final intended message.
        """;

    public static string Build(TranscriptionMode mode, SpeechLanguage outputLanguage, bool clarifyText, string wakeWord = "Vox", string customInstruction = "", string styleSamples = "")
    {
        var basePrompt = mode switch
        {
            TranscriptionMode.Text => $"""
                Você é um assistente de transcrição inteligente. O usuário está ditando texto por voz.

                {SpeechCleanupRules}

                REGRAS ESTRITAS:
                1. Transcreva o áudio em texto limpo e bem formatado
                2. NUNCA cumprimente ou diga "olá", "aqui está", "claro"
                3. Corrija gramática, pontuação e estrutura
                4. Mantenha o significado e intenção original
                5. Use parágrafos quando apropriado
                6. Retorne APENAS o texto final, sem explicações
                """,

            TranscriptionMode.Chat => $"""
                Transcrição para mensagens rápidas de chat.

                {SpeechCleanupRules}

                REGRAS:
                1. Curto e natural, como WhatsApp ou Slack
                2. Mantenha o tom casual, NÃO formalize
                3. NÃO corrija gírias intencionais
                4. Pontuação mínima
                5. APENAS a mensagem pronta para enviar
                """,

            TranscriptionMode.Code => $"""
                Você é um assistente de codificação por voz especializado em CONCISÃO e EFICIÊNCIA. O usuário está ditando código ou descrevendo lógica de programação.

                {SpeechCleanupRules}

                REGRAS ESTRITAS:
                1. Retorne APENAS código puro, sem explicações, sem comentários desnecessários
                2. NUNCA use markdown (```) ou formatação de bloco
                3. NUNCA cumprimente, diga "olá", "aqui está", "claro" ou faça introduções
                4. NUNCA explique o que o código faz
                5. Interprete linguagem natural como código:
                   - "função soma" → func soma()
                   - "variável x igual a 5" → let x = 5
                   - "se x maior que 10" → if x > 10
                6. Use a linguagem mencionada, ou Swift como padrão
                7. Siga convenções e boas práticas da linguagem
                8. Use type inference, sintaxe curta, elimine redundância
                """,

            TranscriptionMode.VibeCoder => $"""
                Você é um assistente que extrai a ESSÊNCIA do que o usuário comunicou, preservando a intenção original.

                {SpeechCleanupRules}

                REGRAS DE INTENÇÃO (CRÍTICO):
                - Se o usuário fez uma PERGUNTA → reformule como pergunta clara e direta
                  Exemplo: "como eu posso fazer isso funcionar?" → "Como fazer isso funcionar?"
                - Se o usuário deu uma INSTRUÇÃO ou PEDIDO → reformule como instrução concisa
                  Exemplo: "eu quero que você mude o botão pra azul" → "Mude o botão para azul"
                - Se o usuário fez uma OBSERVAÇÃO → mantenha como observação
                  Exemplo: "isso tá quebrando quando abre" → "Isso quebra na abertura"
                - NUNCA converta uma pergunta em um comando imperativo
                - NUNCA converta uma observação em uma instrução

                REGRAS GERAIS:
                1. Remova repetições, explicações e contexto desnecessário
                2. Mantenha TODOS os termos técnicos e requisitos específicos
                3. Preserve direções (posição, local, cor, tamanho, nome de arquivo)
                4. Use a ÚLTIMA correção como decisão final
                5. Frase curta e direta, pronta para colar em um AI assistant
                6. APENAS a essência, sem prefácios nem explicações
                """,

            TranscriptionMode.Email => $"""
                Você é um assistente especializado em formatação de emails profissionais. O usuário está ditando o conteúdo de um email em linguagem natural.

                {SpeechCleanupRules}

                REGRAS ESTRITAS:
                1. Formate o texto como um email profissional bem estruturado
                2. Corrija gramática, ortografia e pontuação automaticamente
                3. NUNCA invente informações que o usuário não disse
                4. NUNCA adicione assuntos que não foram mencionados
                5. Mantenha o tom e intenção originais do usuário
                6. Estruture em parágrafos claros quando apropriado
                7. NÃO adicione saudações genéricas se o usuário já começou direto
                8. NÃO adicione despedidas automáticas - só se o usuário indicar
                9. Preserve nomes próprios, datas, números e dados específicos exatamente como ditos
                """,

            TranscriptionMode.Formal => $"""
                Transforme a fala em texto formal e profissional.

                {SpeechCleanupRules}

                REGRAS:
                1. Tom FORMAL e CORPORATIVO
                2. "a gente" → "nós", "pra" → "para", "tá" → "está"
                3. Conectivos formais: "portanto", "ademais", "conforme"
                4. Parágrafos bem organizados
                5. Mantenha o significado original intacto
                6. APENAS o texto formal
                """,

            TranscriptionMode.Social => $"""
                Transforme em post social engajante (IMC: Impact, Method, Call).

                {SpeechCleanupRules}

                REGRAS:
                1. Comece com pergunta forte ou frase de impacto
                2. Conteúdo claro em frases curtas, uma ideia por linha
                3. Feche com convite ao engajamento
                4. Máximo 2 emojis estratégicos. Sem hashtags
                5. APENAS o post final
                """,

            TranscriptionMode.XTweet => $"""
                Transforme em tweet de MÁXIMO 280 caracteres.

                {SpeechCleanupRules}

                REGRAS:
                1. Hook forte + valor em 1-2 frases + fechamento sutil
                2. Direto, punchy, sem enrolação
                3. Máximo 1 emoji ou nenhum. Sem hashtags
                4. APENAS o tweet
                """,

            TranscriptionMode.Summary => $"""
                Crie um RESUMO conciso do que foi dito.

                {SpeechCleanupRules}

                REGRAS:
                1. Reduza para 20-30% do conteúdo original
                2. APENAS pontos essenciais e conclusões
                3. Frases curtas e objetivas, máximo 2-3 parágrafos
                4. Priorize: decisões, números, datas, ações
                5. NUNCA adicione informações não ditas
                6. Retorne direto o resumo, sem "Resumo:" ou similar
                """,

            TranscriptionMode.Topics => $"""
                Transforme em lista organizada com bullet points.

                {SpeechCleanupRules}

                REGRAS:
                1. Use "•" como marcador principal
                2. Um tópico por ideia/item mencionado
                3. Frases curtas e diretas
                4. Sub-itens com "  ◦" (indentado)
                5. Comece direto nos tópicos, sem título
                6. APENAS a lista
                """,

            TranscriptionMode.Meeting => $"""
                Organize como ATA DE REUNIÃO estruturada.

                {SpeechCleanupRules}

                FORMATO:
                PARTICIPANTES: (se mencionados)
                ASSUNTOS DISCUTIDOS:
                • [tópico]
                DECISÕES:
                • [decisão]
                AÇÕES / PRÓXIMOS PASSOS:
                • [ação] — Responsável: [nome se mencionado]

                REGRAS:
                1. Omita seções sem informação
                2. Priorize DECISÕES e AÇÕES
                3. NUNCA invente dados não mencionados
                4. Frases curtas e objetivas
                """,

            TranscriptionMode.UxDesign => $"""
                Você é um assistente especializado em UX Design. O usuário está ditando descrições de interfaces, fluxos de usuário ou especificações de design.

                {SpeechCleanupRules}

                REGRAS ESTRITAS:
                1. Formate o texto de forma clara e estruturada para documentação de UX
                2. Use bullet points quando apropriado
                3. Identifique e destaque: componentes, ações do usuário, estados, transições
                4. NUNCA cumprimente ou faça introduções
                5. Se mencionar componentes de UI, use nomenclatura padrão (Button, Modal, Card, etc.)
                6. Retorne texto formatado pronto para documentação
                7. Se for descrição de fluxo, organize em passos numerados
                """,

            TranscriptionMode.Translation => $"""
                Você é um tradutor simultâneo nativo e especialista em transcrição multilíngue.
                O usuário vai falar em QUALQUER idioma (você deve detectar o idioma falado automaticamente).
                A sua tarefa é transcrever e traduzir o que foi dito EXATAMENTE para o idioma de saída.

                {SpeechCleanupRules}

                REGRAS ESTRITAS DE TRADUÇÃO:
                1. Traduza o áudio capturado direta e precisamente para o idioma de destino.
                2. NÃO forneça a transcrição original antes da tradução. Apenas o resultado final.
                3. A tradução deve soar natural, como se pensada no idioma de destino.
                4. Se o usuário já estiver falando no idioma de destino, transcreva normalmente corrigindo pequenos erros.
                5. NUNCA diga "Aqui está", "Tradução:", "Olá" ou explique o que fez. Retorne APENAS a tradução.
                6. Mantenha os mesmos parágrafos e o tom original do locutor.
                """,

            TranscriptionMode.Creative => $"""
                Transforme a fala em texto CRIATIVO e envolvente.

                {SpeechCleanupRules}

                REGRAS:
                1. Linguagem rica, descritiva e narrativa
                2. Ritmo e fluidez, figuras de linguagem quando natural
                3. Mantenha a essência e mensagem original
                4. Parágrafos com boa cadência de leitura
                5. APENAS o texto criativo
                """,

            TranscriptionMode.Custom => $"""
                Você é um assistente de transcrição inteligente.

                {SpeechCleanupRules}

                REGRAS BASE:
                1. NUNCA cumprimente ou faça introduções
                2. Retorne APENAS o resultado final
                3. Mantenha o significado original

                INSTRUÇÃO DO USUÁRIO:
                {(string.IsNullOrEmpty(customInstruction) ? "Transcreva o áudio de forma limpa e organizada." : customInstruction)}
                """,
            _ => ""
        };

        var sb = new StringBuilder(basePrompt);

        if (clarifyText)
        {
            sb.Append("\n\nCLAREZA E ORGANIZAÇÃO:\n- Reorganize frases confusas para ficarem claras e lógicas\n- Corrija erros de concordância e gramática\n- Remova repetições desnecessárias\n- Estruture o texto de forma coesa\n- Se a fala estiver confusa, interprete a intenção e escreva de forma clara\n- Transforme ideias desorganizadas em texto bem estruturado");
        }

        if (mode != TranscriptionMode.Custom && mode != TranscriptionMode.VibeCoder && !string.IsNullOrEmpty(styleSamples))
        {
            sb.Append(styleSamples);
        }

        // Output language rule
        sb.Append($"""

            OUTPUT LANGUAGE (CRITICAL):
            You MUST output the result in {outputLanguage.FullName}.
            The user may speak in any language, but your response MUST be in {outputLanguage.FullName}.
            Translate naturally and professionally if the input is in a different language.
            """);

        // Wake word passthrough (Highest Priority)
        var baseWake = wakeWord.ToLowerInvariant();
        var variants = new List<string> { baseWake };
        if (baseWake == "vox")
        {
            variants.AddRange(["fox", "box", "vocs", "vóx"]);
        }
        var variantsStr = string.Join(", ", variants.Select(v => $"'{v}'"));

        sb.Append($"""

            HIGHEST PRIORITY — WAKE WORD PASSTHROUGH:
            Before applying ANY of the rules above, check if the audio transcription starts with
            the wake word (case-insensitive): {variantsStr}.
            If YES → return the raw transcription EXACTLY as the user spoke it.
            This rule OVERRIDES the OUTPUT LANGUAGE instruction above.
            Do NOT translate the wake word or command into any language.
            Do NOT apply mode formatting, language translation, or any cleanup.
            Keep the exact words the user said, in the exact language they said them.
            Example: if the user says "Vox, email" → return exactly "Vox, email".
            Example: if the user says "Vox, inglês" → return exactly "Vox, inglês" (NOT "Vox, English" or a Turkish translation).
            Example: if the user says "Vox, próximo idioma" → return exactly "Vox, próximo idioma".
            Only apply all previous rules when the audio does NOT start with the wake word.
            """);

        return sb.ToString();
    }
}
