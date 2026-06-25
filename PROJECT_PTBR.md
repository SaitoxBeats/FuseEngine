# FuseEngine & Blowtorch: Documentação do Projeto

Este documento fornece uma visão técnica abrangente da base de código do **FuseEngine** e de seu editor de mapas complementar, o **Blowtorch**. Ele descreve a arquitetura geral, os componentes individuais de cada subsistema, as estruturas de código, os fluxos de dados e apresenta guias detalhados sobre como modificar, estender e depurar qualquer parte do sistema.

---

## 1. Visão Geral do Projeto

O projeto está estruturado como uma única solução C# (`FuseEngine.slnx`) que tem como alvo o **.NET 10.0**. Ele consiste em dois assemblies executáveis principais que compartilham bibliotecas:

1. **[Fuse](file:///e:/DEV/Csharp/FuseEngine/Fuse)**: Uma biblioteca de engine de jogo 3D leve e cliente jogável. Ela lida com renderização 3D (OpenGL), simulação de física (Jolt Physics), controle virtual do personagem, interação do jogador, exibição de HUD e serialização/deserialização de mapas do jogo.
2. **[Blowtorch](file:///e:/DEV/Csharp/FuseEngine/Blowtorch)**: Um editor de mapas desktop 3D/2D construído sobre a base de código da engine Fuse. Ele possui um layout de janela com quatro viewports (Perspectiva 3D, Topo, Frente, Lateral), gizmos de translação/rotação/escala, inspeção da hierarquia do nível, edição direta do documento JSON bruto e um sistema de desfazer/refazer baseado em snapshots.

### Principais Tecnologias e Bibliotecas Utilizadas
* **Silk.NET**: Fornece vinculações (bindings) C# de alta performance para:
  * **GLFW** (`Silk.NET.GLFW`) - Criação de janelas, polling de entrada (input) e roteamento de eventos.
  * **OpenGL** (`Silk.NET.OpenGL`) - Pipeline de renderização gráfica moderna (OpenGL versão 3.3 Core Profile).
  * **Assimp** (`Silk.NET.Assimp`) - Utilitário para importação de modelos e assets 3D.
* **Jolt Physics** (`JoltPhysicsSharp`): Engine de física C++ de alta performance encapsulada para C#. Usada para simulação de corpos rígidos, detecção de colisões, raycasting e controladores de personagens virtuais.
* **ImGui.NET**: Wrapper C# para Dear ImGui. Usado para o console de desenvolvedor no jogo e para toda a interface de usuário do editor.
* **StbImageSharp**: Biblioteca de carregamento de imagens para leitura de arquivos de textura (BMP, PNG, JPEG, etc.).

### Estilo de Renderização e Estética
* As texturas são mapeadas utilizando filtragem por vizinho mais próximo (`GLEnum.NearestMipmapNearest` e `GLEnum.Nearest`) em vez de interpolação linear, proporcionando uma estética pixelada retrô.

---

## 2. Estrutura de Diretórios

```text
FuseEngine/
├── FuseEngine.slnx               # Configuração da Solução C#
├── Fuse/                         # Núcleo da Engine (Game Engine)
│   ├── Fuse.csproj               # Configuração do Projeto (.NET 10.0, Pacotes Nuget)
│   ├── Program.cs                # Ponto de Entrada do Cliente
│   ├── res/                      # Diretório de Recursos (Shaders, Texturas, Mapas, Modelos)
│   └── src/                      # Código Fonte
│       ├── Core/                 # Loop da engine, contexto de janela, logger, ResPath
│       ├── AssetManagement/      # Cache de recursos e carregadores
│       ├── Renderer/             # Wrappers do OpenGL, câmera, grafo de cena, HUD, debug drawer
│       ├── Input/                # Polling de teclado/mouse via GLFW e utilitários de estado
│       ├── Physics/              # Wrappers do Jolt Physics, filtros e configurações de corpos rígidos
│       ├── Player/               # Controlador virtual do jogador e mecânicas de coleta (pickup)
│       ├── Interaction/          # Triggers de raycast e implementações de IInteractable
│       ├── Scene/                # Serialização/deserialização de dados do mapa
│       └── UI/                   # Componentes de HUD (fontes, textos, texturas)
└── Blowtorch/                    # Aplicativo Editor de Mapas
    ├── Blowtorch.csproj          # Configuração do Projeto (Depende do Fuse)
    ├── Program.cs                # Ponto de Entrada do Editor
    ├── CommandHistory.cs         # Histórico de comandos de Desfazer/Refazer
    ├── EditorApplication.cs      # Setup do editor, loop principal e updates de viewports
    ├── EditorAssetService.cs     # Caches de malhas/texturas dedicados às visualizações do editor
    ├── EditorGizmo.cs            # Gizmos customizados de translação, rotação e escala
    ├── EditorInputService.cs     # Roteamento de entradas da janela para ImGui e Viewports
    ├── EditorSceneService.cs     # Ponte entre o documento e a cena lógica
    ├── EditorUI.cs               # Janelas ImGui, barra de ferramentas, hierarquia e editor JSON
    ├── EditorViewport.cs         # Desenho de viewports baseadas em FBO, grades ortho e câmera
    └── ViewportCamera.cs         # Controlador de câmera do editor (órbita, voo, zoom, pan)
```

---

## 3. Arquitetura da Engine (Fuse)

### 3.1. Ciclo de Vida Principal da Engine (`Fuse.Core`)

O ciclo de vida do cliente do jogo é gerenciado pela classe [Application.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Core/Application.cs).

* **Caminhos de Recursos (`ResPath.cs`)**:
  * Inclui um algoritmo de busca que varre caminhos pais em busca do diretório `res`. Ele busca recursivamente dentro de subdiretórios dos caminhos candidatos se necessário, permitindo uma execução robusta independentemente da estrutura do diretório de trabalho.
* **Inicialização (`Init()`)**:
  1. Exibe um logotipo ASCII azul no console se o arquivo `res/splash.txt` existir.
  2. Cria uma [Window](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Core/Window.cs), inicializando o GLFW e o contexto modern OpenGL.
  3. Configura o [AssetManager](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/AssetManagement/AssetManager.cs), o [DebugDrawer](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Debug/DebugDrawer.cs) e o [UIRenderer](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/UIRenderer.cs).
  4. Inicializa o [PhysicsWorld](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Physics/PhysicsWorld.cs) (definindo parâmetros do sistema Jolt e tabelas de filtragem).
  5. Spawna o [Player](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/Player.cs) e registra o [PickupController](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/PickupController.cs) para manuseio físico de caixas.
  6. Pré-carrega shaders, texturas e primitivos padrão (cubo, plano de terra).
  7. Carrega o mapa ativo a partir do arquivo usando o [MapSerializer](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/MapSerializer.cs).
  8. Registra os ouvintes (listeners) de eventos de redimensionamento de janela, rolagem e movimento do mouse.
* **Loop do Jogo (`Run()`)**:
  * Executa um loop contínuo `while (!_window.ShouldClose)`.
  * Calcula o delta-time (`dt`) de cada frame via [Engine.Tick](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Core/Engine.cs).
  * Atualiza os arrays de polling de input.
  * Se o jogo não estiver pausado:
    * Atualiza o carregamento de objetos baseado em física (`_pickup.PhysicsUpdate(dt)`).
    * Avança a simulação física (`_physics.Step(dt)`).
    * Atualiza movimentações do jogador, restrições de agachamento e matrizes da câmera (`_player.Update(dt)`).
    * Atualiza os scripts de interação ativos (`interactable.Update(dt)`).
    * Resolve comandos gerais de entrada (ex: pausar, abrir console, desenhar colisores, salvar mapa).
  * Realiza o pipeline de renderização:
    1. Limpa os buffers.
    2. Renderiza o cubo do skybox usando configurações específicas de desativação de escrita no depth buffer.
    3. Renderiza a geometria visível do cenário utilizando o shader de iluminação principal.
    4. Renderiza as malhas de colisão física (wireframe) se o debug drawer (`F9`) estiver ativo.
    5. Renderiza a interface 2D do HUD (mira, taxa de frames por segundo, tela de pause) com blending ativo.
    6. Desenha elementos do Dear ImGui (console do desenvolvedor, inspetor do player contendo controle deslizante para ajustar o FOV em tempo real).
    7. Executa o swap de buffers e consulta atualizações de eventos do sistema operacional.

### 3.2. Sistema de Renderização (`Fuse.Renderer`)

* **[Mesh.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Mesh.cs)**: Encapsula um Vertex Array Object (VAO), Vertex Buffer Object (VBO) e Element Buffer Object (EBO) do OpenGL. Os vértices são estruturados usando a struct `Vertex` (Position, TexCoord, Normal). Oferece funções estáticas para gerar geometrias básicas padrões (cubos e planos texturizados).
* **[Shader.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Shader.cs)**: Compila os códigos fonte de shaders de vértices e fragmentos GLSL. Inclui métodos utilitários para localizar e configurar parâmetros uniformes (ex: `SetMat4`, `SetVec3`, `SetFloat`, `SetBool`).
* **[Texture.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Texture.cs)**: Carrega imagens locais para a memória usando a biblioteca `StbImageSharp`, configura parâmetros de amostragem e filtragem (filtragem por vizinho mais próximo para visuais retrô pixelados) e envia os pixels para texturas da GPU.
* **[Camera.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Camera.cs)**: Controla as matrizes de câmera. Gera matrizes de projeção perspectiva baseadas no campo de visão (FOV) e proporção de tela (aspect ratio), e monta matrizes de Visualização (View). Suporta propriedades configuráveis de planos próximo e distante (`NearPlane`, `FarPlane`), definindo o plano de corte distante padrão em `1000.0f` para evitar o corte visual do ambiente em mapas extensos.
* **[Scene.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Scene.cs)** & **[Entity.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Scene.cs#L18)**: Estrutura uma lista de nós de renderização no cenário. As entidades (`Entity`) associam modelos visuais (`Mesh`), caminhos de texturas, posições/escalas (`Transform`) e referências de física (`RigidBody`). Durante a renderização, a matriz do transform é atualizada com a posição e rotação reais provenientes do corpo físico. A escala visual (`ModelScale`) é manipulada dinamicamente no momento da renderização e da construção física, garantindo que os vértices dos assets em cache permaneçam intocados em sua escala original 1.0.

### 3.3. Gerenciamento de Entrada (`Fuse.Input`)

Gerenciado pela classe estática [Input.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Input/Input.cs). Copia o estado dos botões (`s_keysDown` para `s_keysPrev`) no início de cada frame.
* **`KeyPressed(int key)`**: Retorna `true` apenas no frame em que a tecla foi pressionada pela primeira vez.
* **`KeyDown(int key)`**: Retorna `true` continuamente enquanto a tecla estiver sendo pressionada.
* **Deslocamento do mouse**: Mede a variação do mouse de um frame para o outro (`MouseOffsetX`, `MouseOffsetY`), essencial para o cálculo de rotação suave da câmera de primeira pessoa.
* O estado do cursor de mouse pode ser modificado com `DisableCursor()` (mouse invisível e preso ao centro) ou `ShowCursor()` (mouse livre para interação com menus).

### 3.4. Simulação Física (`Fuse.Physics`)

* **[PhysicsWorld.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Physics/PhysicsWorld.cs)**: Inicializa o Jolt Foundation e os pools de processamento paralelo. Define as estruturas de filtragem de colisão (`BroadPhaseLayerInterfaceTable`, `ObjectLayerPairFilterTable`, `ObjectVsBroadPhaseLayerFilterTable`). Expõe assinaturas para criar, aplicar passos (ticks), consultar e destruir corpos rígidos da simulação.
* **[RigidBody.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Physics/RigidBody.cs)**: Implementa um padrão de builder para configurar atributos físicos antes de instanciá-los no Jolt. Suporta os seguintes formatos de colisão:
  * **Box**: Requer dimensões de meia-extensão (half-extents).
  * **Sphere**: Requer o raio.
  * **Capsule**: Requer altura total e raio.
  * **Plane**: Superfície infinita configurada por normal e distância.
  * **Trimesh**: Malha arbitrária estática construída a partir de listas de vértices e índices. Para evitar que personagens tropecem em arestas planas internas ("ghost collisions" / colisões fantasmas), corpos trimesh configuram `ActiveEdgeMode.CollideOnlyWithActive` e calculam arestas ativas usando um limite `CosThresholdAngle`. Além disso, a escala (`ModelScale`) é multiplicada dinamicamente aos vértices durante a etapa `Build()`, separando a escala física dos dados originais do modelo.
  * Por padrão, corpos com massa definida como `0` são montados como estáticos (`MotionType.Static`), e passam a usar movimentação dinâmica (`MotionType.Dynamic`) com cálculo automático de inércia quando possuem massa positiva.

### 3.5. Controlador do Jogador (`Fuse.Player`)

* **[Player.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/Player.cs)**: Controla a movimentação em primeira pessoa utilizando o controlador de personagem virtual nativo do Jolt (`CharacterVirtual`).
  * **Física de Movimentação**: Implementa um modelo de aceleração com atrito baseado no estilo do clássico jogo Quake. Movimentações no chão calculam a velocidade ideal com desaceleração exponencial. No ar, a aceleração é limitada para permitir o controle do salto sem perder a inércia acumulada.
  * **Salto e Agachamento**: Pressionar `Space` aplica um impulso instantâneo no eixo Y. Agachar (`Left Control`) reduz a altura da cápsula do personagem com `SetShape()`. Se o jogador soltar a tecla de agachamento em espaços apertados, a física faz um teste de raio vertical (`NarrowPhaseQuery.CastRay()`); caso seja detectado um teto, o personagem é mantido agachado para evitar que ele fique preso.
  * **Noclip (`F1`)**: Ativa o modo de voo livre para depuração. Desativa as colisões do personagem e desloca a câmera diretamente pela cena por meio das teclas direcionais.
  * **Empurrar Objetos**: O método `PushDynamicBodies()` lê os contatos ativos do `CharacterVirtual` e aplica forças proporcionais às massas dos corpos dinâmicos tocados, permitindo que o jogador empurre caixas ao andar contra elas.
* **[PickupController.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/PickupController.cs)**: Permite segurar e arremessar objetos.
  * Pressionar `E` projeta um raio a partir da câmera. Se interceptar um corpo dinâmico, ele é selecionado.
  * Enquanto o objeto é segurado, o fator de gravidade dele é zerado (`SetGravityFactor(0.0f)`).
  * A cada frame do passo de física, calcula-se o vetor de distância entre a posição de repouso em frente à câmera e o centro de massa do objeto. A partir disso, aplica-se uma força de atração linear (`AddForce()`) simulando uma mola física.
  * Soltar o botão (`E`) ou clicar com o botão esquerdo (`Left Click`) devolve o fator de gravidade original e aplica um impulso de arremesso combinado com o momento de rotação do mouse no frame atual.

### 3.6. Sistema de Interação (`Fuse.Interaction`)

* **[InteractionSystem.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction/InteractionSystem.cs)**: Faz a ponte entre corpos de física e classes lógicas C#. Como a física do Jolt possui apenas ponteiros numéricos de `UserData`, o sistema aloca uma referência fixa do C# (`GCHandle.Alloc`) apontando para a interface [IInteractable](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction/Interactable.cs), salvando o endereço no corpo rígido.
* **[IInteractable.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction/Interactable.cs)**: A interface exige a seguinte estrutura:
  ```csharp
  public interface IInteractable
  {
      void OnInteract();
      void Update(float dt);
      Renderer.Entity? Entity { get; set; }
      Physics.PhysicsWorld? World { get; set; }
  }
  ```
  * Interagíveis ativos são mantidos pela `Application` e recebem atualizações lógicas a cada frame (`Update(dt)`).
  * Modificações de interação padrão:
    * `ButtonInteract`: Emite logs de depuração no console do sistema.
    * `CubeInteract`: Emite logs de depuração no console do sistema.
    * `DoorInteract`: Executa uma animação suave de abertura e fechamento da porta nos eixos locais utilizando interpolação de quatérnios (`Quaternion.Slerp`). Aplica uma curva Ease-out cúbica na abertura, e uma curva Ease-in quadrática no fechamento. Atualiza o transform do corpo rígido físico com `World.SetBodyPositionAndRotation`.
* **Raio de Seleção**: O cliente atualiza a mira do jogador a cada frame projetando um raio curto (5 unidades). Ao colidir com corpos que possuam referências de interação válidas, a mira altera sua textura. Pressionar `E` resgata o objeto C# através de seu ponteiro e executa o método `OnInteract()`.
* **Coleta de Lixo**: Ao descarregar mapas, os `GCHandle` de cada interactable são liberados explicitamente (`Free()`) para evitar vazamentos de memória na heap gerenciada.

### 3.7. Serialização e Geometria de Brushes (`Fuse.Scene` & `Model`)

Os mapas de níveis são guardados em formato JSON por meio de rotinas no [MapSerializer.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/MapSerializer.cs) que preenchem a estrutura de dados [MapDocument.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/Model/MapDocument.cs).

* **Modelos vs. Brushes**:
  * **Modelos**: Malhas pré-carregadas de arquivos externos (como `.obj`) ou primitivos procedurais internos.
  * **Brushes (Pincéis)**: Geometrias convexas personalizadas definidas a partir de planos tridimensionais, seguindo o padrão de design clássico de editores de jogos retrô.
* **[Brush.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/Model/Brush.cs) & [Face.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/Model/Face.cs)**: Um brush contém uma coleção de instâncias de faces, onde cada face descreve a equação matemática de um plano no espaço:
  $$\text{Normal} \cdot \vec{P} + D = 0$$
  As faces contêm também dados de mapeamento UV (`UAxis`, `VAxis`), parâmetros de escala, rotação, deslocamento e a textura associada.
* **[MeshGenerator.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/Model/MeshGenerator.cs)**: Executa o algoritmo de montagem de malhas para Brushes:
  1. **Intersecção de Planos**: Calcula a intersecção de todas as combinações de três planos do brush. Apenas os pontos que estiverem atrás ou exatamente na borda de todos os outros planos do brush são mantidos como vértices válidos.
  2. **Ordenação dos Polígonos**: Para cada face do brush, reúne todos os pontos válidos gerados que pertencem a ela, calcula a média deles (centro) e ordena-os em sentido anti-horário com base no vetor normal da face.
  3. **Mapeamento UV**: Projeta as posições tridimensionais dos vértices ordenados nos eixos UV (`UAxis`, `VAxis`) da face correspondente, aplicando os coeficientes de escala, rotação e deslocamento.
  4. **Triangulação**: Constrói os triângulos finais para renderização montando uma estrutura de leque (triangle fan) partindo do primeiro vértice ordenado de cada face.

---

## 4. Arquitetura do Editor (Blowtorch)

### 4.1. Ciclo de Vida do Editor (`EditorApplication.cs`)

O editor de mapas é gerenciado pela classe principal [EditorApplication.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorApplication.cs).

* **Inicialização**:
  1. Exibe um logotipo ASCII azul no console se o arquivo `res/splash.txt` existir.
  2. Instancia o invólucro de janela GLFW via [EditorWindow](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorWindow.cs).
  3. Cria o serviço de controle de inputs [EditorInputService](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorInputService.cs).
  4. Prepara a renderização do Dear ImGui com o [ImGuiBackEnd](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Imgui/ImGuiBackEnd.cs).
  5. Ativa o carregador de recursos [EditorAssetService](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorAssetService.cs) e a gerência do mapa atual com o [EditorSceneService](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorSceneService.cs).
  6. Instancia quatro viewports independentes ([EditorViewport](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorViewport.cs)) e o gerenciador de comandos [CommandHistory](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/CommandHistory.cs).
* **Loop Principal**:
  * Mede o tempo decorrido por frame.
  * Direciona os inputs da janela para a interface ImGui.
  * Solicita que a GPU direcione a saída gráfica para os Framebuffer Objects (FBO) de cada um dos quatro viewports.
  * Renderiza grades de auxílio, formas sólidas e contornos wireframe de acordo com o modo da viewport ativa (2D Ortho ou 3D Perspective), aplicando linhas extras de depuração como setas de spawn e colisores.
  * Invoca o desenho do workspace com o [EditorUI.Draw](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorUI.cs#L89), acoplando os texturas renderizados dos FBOs dentro de janelas ImGui acopláveis.

### 4.2. Viewports do Editor (`EditorViewport.cs`)

O controle de visualizações tridimensionais e bidimensionais utiliza a classe [ViewportCamera.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/ViewportCamera.cs).
* **Pipeline FBO**: Cada viewport aloca um Framebuffer Object (FBO) no OpenGL com textura de cores e buffer de profundidade independentes. Quando a cena é desenhada, o FBO é ativado, os comandos OpenGL de desenho de cena são submetidos e a textura final gerada é anexada à janela correspondente no Dear ImGui.
* **Modos de Visualização**:
  * **Perspective 3D**: Câmera perspectiva clássica. O botão direito do mouse rotaciona a câmera ao redor de um ponto de órbita focal; o botão do meio realiza translação (pan); a rolagem do mouse aplica zoom. Manter o botão direito pressionado altera os controles para modo de voo livre via `W`/`A`/`S`/`D`/`Q`/`E`.
  * **Ortográficas (Top, Front, Side)**: Projeções planas paralelas nos eixos Y, Z e X. Nestas viewports a câmera não pode orbitar. Os objetos são exibidos no modo de contorno wireframe (`GLEnum.Line`) facilitando o alinhamento de blocos e brushes.

### 4.3. Gizmos de Transformação (`EditorGizmo.cs`)

Responsável pelo desenho e interação de manipuladores espaciais via código na classe estática [EditorGizmo.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorGizmo.cs).

* **Projeção de Coordenadas**: Transforma pontos tridimensionais do espaço global em pixels bidimensionais da tela com base nas matrizes da viewport ativa:
  $$P_{clip} = M_{proj} \cdot M_{view} \cdot P_{world}$$
  $$P_{screen} = \left(\frac{P_{clip}.x}{P_{clip}.w}, \frac{P_{clip}.y}{P_{clip}.w}\right)$$
* **Axis Selection**: Desenha e detecta interseção nos eixos X (Vermelho), Y (Verde) e Z (Azul). Medindo a distância do cursor do mouse em relação ao segmento da linha projetada do gizmo na tela, determina-se qual eixo está sob foco.
* **Manipulação**: Ao clicar e arrastar, uma linha de projeção em raio (`ScreenToWorldRay`) calcula a posição correspondente do mouse no espaço de trabalho. O incremento resultante é repassado ao transform do objeto com suporte opcional a travamento em grade fixa (`snapAmount`).
* **Rotação e Escala**: O gizmo de escala altera as dimensões físicas dos objetos. O gizmo de rotação calcula a variação angular projetando as posições do mouse no plano normal do eixo rotacionado:
  $$\text{Ângulo} = \text{atan2}(Y, X)$$

### 4.4. Histórico de Comandos e Undo/Redo (`CommandHistory.cs`)

O editor gerencia modificações por meio do padrão de projeto Command em [CommandHistory.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/CommandHistory.cs).
* **`ICommand`**: Declara assinaturas básicas para `Execute()` (aplicar/refazer) e `Undo()` (desfazer).
* **`SnapshotCommand`**: Antes de iniciar uma ação que altere o mapa, o editor gera uma cópia do documento no formato texto JSON (`stateBefore`). Ao encerrar a ação, gera-se uma nova cópia do estado atualizado (`stateAfter`). O comando de snapshot armazena ambos os textos.
* **Desfazer / Refazer**: Ao desfazer, o editor lê a string do estado anterior, reconstrói o `MapDocument` utilizando o parser JSON, limpa as referências de malha ativas e remonta as entidades visuais. O processo inverso ocorre ao refazer.

### 4.5. Painéis de Interface do Editor (`EditorUI.cs`)

O layout do editor é construído usando o ImGui:
* **Menu Superior**: Ações de arquivo (Novo, Carregar, Salvar, Jogar standalone `F5`, Sair), histórico de desfazer/refazer e opções gerais de depuração de cena.
  * **Jogar standalone (`F5`)**: Salva o mapa ativo automaticamente, localiza o executável compilado da engine (`Fuse.exe`) e o inicia em um processo separado, alimentando-o com o mapa aberto no editor.
* **Scene Hierarchy & Importador de Modelos**: Lista todas as entidades cadastradas no mapa. O clique esquerdo seleciona o objeto e o clique direito abre o menu de contexto.
  * **Modal de Importação de Modelos**: Clicar em "Add Model" exibe uma janela pop-up que lista os arquivos `.obj` no diretório `res/Models/`.
    1. Escolhe o arquivo `.obj`.
    2. Lê o arquivo e detecta automaticamente arquivos de materiais `.mtl` descritos na tag `mtllib`.
    3. Abre o `.mtl` correspondente e descobre o arquivo de textura difusa mapeado em `map_Kd`.
    4. Verifica se a textura correspondente existe na pasta `res/Textures/` e mapeia automaticamente os parâmetros de textura do objeto.
    5. Configura o modelo carregado por padrão como um corpo físico estático do tipo `MapShapeType.Trimesh` com massa igual a zero.
* **Properties Inspector (Inspector)**: Exibe e altera campos específicos da entidade focada, tais como posição, escala, rotação, texturas, scripts de interação e parâmetros de corpos físicos (massa, atrito e restituição).
* **Brush Builder**: Oferece utilitários para ajustar extensões e recalcular planos e faces UV de brushes, permitindo converter estruturas modificadas em malhas atualizadas de colisão e render.
* **JSON Source Viewer**: Exibe a estrutura de dados bruta do arquivo de nível em formato textual atualizada em tempo real.

---

## 5. Guias de Desenvolvimento e Extensão

### 5.1. Adicionando um Novo Objeto Interagível

Siga os passos abaixo para criar um script customizado de interação (ex: abrir uma porta ou coletar uma chave):

1. **Crie a Classe**: Crie um novo arquivo C# no diretório [Fuse/src/Interaction/](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction) (ex: `DoorInteract.cs`).
2. **Implemente `IInteractable`**: Adicione as propriedades requeridas e o atributo de classe correspondente indicando o nome identificador:
   ```csharp
   using System.Numerics;
   using Fuse.Core;

   namespace Fuse.Interaction;

   [InteractableType("DoorInteract")]
   public sealed class DoorInteract : IInteractable
   {
       public Renderer.Entity? Entity { get; set; }
       public Physics.PhysicsWorld? World { get; set; }

       private Quaternion _baseRot;
       private bool _baseSet;
       private bool _open;
       private bool _animating;
       private float _elapsed;
       private float _duration = 1.0f;
       private Quaternion _from;
       private Quaternion _to;

       public void OnInteract()
       {
           if (Entity?.Body == null || !Entity.Body.IsBuilt || World == null || _animating)
               return;

           if (!_baseSet)
           {
               _baseRot = Entity.Body.Rotation(World);
               _baseSet = true;
           }

           _open = !_open;
           _animating = true;
           _elapsed = 0f;
           _from = Entity.Body.Rotation(World);
           _to = _baseRot * Quaternion.CreateFromAxisAngle(Vector3.UnitY, _open ? 1.57f : 0f);
       }

       public void Update(float dt)
       {
           if (!_animating || Entity?.Body == null || !Entity.Body.IsBuilt || World == null)
               return;

           _elapsed += dt;
           float t = float.Clamp(_elapsed / _duration, 0f, 1f);
           float eased = _open ? 1f - MathF.Pow(1f - t, 3f) : t * t; // Curvas de ease-out/ease-in

           var rot = Quaternion.Slerp(_from, _to, eased);
           World.SetBodyPositionAndRotation(Entity.Body.Native, Entity.Body.Position(World), rot);

           if (t >= 1f)
           {
               _animating = false;
               World.BodyInterface.SetAngularVelocity(Entity.Body.Native, Vector3.Zero);
           }
       }
   }
   ```
3. **Configure no Editor**:
   * Abra o mapa no **Blowtorch**.
   * Selecione a entidade correspondente na árvore de objetos.
   * No **Properties Inspector**, preencha o campo **Interactable** com o valor `DoorInteract`.
   * Configure um componente de física (**Body**) na entidade para que ela possa registrar colisões de raio com a câmera.
   * Salve o mapa.

### 5.2. Adicionando um Novo Asset no Projeto

1. **Importação de Arquivos**:
   * Salve arquivos de malhas 3D (formatos OBJ, FBX) em `Fuse/res/Models/`.
   * Salve arquivos de imagem de textura (PNG, BMP, JPG) em `Fuse/res/Textures/`.
2. **Carregando via Código**:
   ```csharp
   // Carregamento de textura
   var minhaTextura = assets.GetTexture($"{ResPath.Path}/Textures/minha_textura.png");

   // Carregamento de modelo 3D (a importação do Assimp cria vértices e faces lógicas automaticamente)
   var meuModelo = assets.GetModel($"{ResPath.Path}/Models/meu_modelo.obj");
   ```
3. **Referenciando no Arquivo de Mapa JSON**:
   ```json
   {
     "id": "minha_estatua",
     "visible": true,
     "model": "Models/meu_modelo.obj",
     "model_scale": 1.0,
     "texture": "Textures/minha_textura.png",
     "body": {
       "shape": "trimesh",
       "position": [0, 2, 0],
       "rotation": [1, 0, 0, 0],
       "mass": 0
     }
   }
   ```

### 5.3. Modificando o Código de Movimentação do Jogador

A física e translação do jogador estão descritas no método `ApplyMovement()` da classe [Player.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/Player.cs#L134).

* **Ajustando Valores Padrões**: Edite as constantes do cabeçalho ou inicializações no construtor:
  * `_maxSpeedGround` (Padrão: `4.0f`): Velocidade padrão de deslocamento no chão.
  * `_jumpForce` (Padrão: `3.8f`): Intensidade do pulo vertical.
  * `_frictionValue` (Padrão: `4.0f`): Coeficiente de atrito de parada no chão.
  * `_airAccel` (Padrão: `150.0f`): Facilidade de alteração de rota aérea.
* **Exemplo de Implementação de Salto Duplo (Double Jump)**:
  1. Declare a variável de controle: `private int _jumpCount = 0;`.
  2. Zere o contador ao detectar solo:
     ```csharp
     if (onGround) {
         _jumpCount = 0;
     }
     ```
  3. Permita o salto ao detectar clique da tecla de pulo mesmo no ar, desde que o contador seja menor que 2:
     ```csharp
     if (Input.Input.KeyPressed(Input.KeyCodes.Space) && (!onGround && _jumpCount < 2)) {
         velocity.Y = _jumpForce;
         _jumpCount++;
     }
     ```

### 5.4. Criando uma Nova Janela de Interface no Editor

Novas janelas ImGui devem ser acopladas no método `Draw()` de [EditorUI.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorUI.cs).

1. **Estruture o Desenho**: Insira a chamada de desenho da janela no loop de desenho:
   ```csharp
   if (ImGui.Begin("Meu Painel"))
   {
       ImGui.Text("Minha Ferramenta Personalizada");
       if (ImGui.Button("Resetar Posição da Seleção"))
       {
           // Insira ações de manipulação de dados de cena aqui
       }
       ImGui.End();
   }
   ```
2. **Aplicação Segura de Estados**: Utilize as referências de serviço injetadas no método (`EditorSceneService`, `EditorAssetService`) para alterar o mapa ativo e lembre-se de registrar as alterações na pilha de comandos para não quebrar a lógica de desfazer/refazer (Undo/Redo).

---

## 6. Dicas de Arquitetura e Física

### Regras de Ciclo de Vida do Jolt Physics
> [!IMPORTANT]
> A simulação do Jolt Physics utiliza alocações unmanaged de C++. Siga estas regras estritamente:
> * Invocar `BodyInterface.DestroyBody(bodyID)` é obrigatório para remover corpos da simulação física antes de liberar ponteiros.
> * Sempre execute o método `Dispose()` de estruturas de física (`PhysicsSystem`, `JobSystemThreadPool`, shapes, controladores de personagem) ao encerrar a aplicação para evitar vazamento de memória do sistema.
> * Mantenha referências ativas em variáveis gerenciadas das instâncias de filtros de colisão (`BroadPhaseLayerInterfaceTable`, `ObjectLayerPairFilterTable`). Caso sejam coletadas pelo Garbage Collector com a física em execução, a aplicação sofrerá travamentos por violação de acesso na DLL nativa.

### Filtragem de Colisão e Colisões Fantasmas
Por padrão, a engine usa uma tabela simplificada de camadas:
* Corpos rígidos e o jogador são criados na camada de objeto padrão `0` (`ObjectLayer`).
* No construtor de `PhysicsWorld`, a chamada `_objectLayerFilter.EnableCollision(0, 0)` habilita colisões gerais entre todos os elementos da camada `0`.
* Se for necessário criar novas camadas (ex: separar colisores invisíveis de gatilho da colisão do jogador), você precisará expandir as tabelas de intersecção registradas no construtor do `PhysicsWorld`.

Ao construir corpos **Trimesh**, tenha cuidado com **Colisões Fantasmas (Ghost Collisions)**. Personagens arrastando-se por um chão plano formado por vários triângulos podem colidir incorretamente com as arestas internas, causando engasgos na movimentação. Para resolver isso, a configuração `ActiveEdgeMode` do Jolt deve ser utilizada na declaração do Trimesh para desativar colisões em arestas internas.

### Limitações de Brushes CSG
> [!TIP]
> O algoritmo do `MeshGenerator` resolve faces por meio de corte de planos convexos.
> * Brushes precisam ser **convexos**. Brushes concavos falharão na ordenação dos polígonos e criarão distorções de renderização.
> * Para criar salas ou áreas internas côncavas (como corredores ou salas), agrupe vários brushes convexos em formato de blocos separados para montar as paredes.
> * Ao editar equações de planos programaticamente, certifique-se de que os planos se fecham em um volume finito. Planos paralelos ou que apontam para direções opostas não formam intersecções fechadas, resultando em listas de vértices vazias e na falha de criação da malha.
