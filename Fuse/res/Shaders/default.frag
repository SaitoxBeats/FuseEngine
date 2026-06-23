#version 330 core
in vec2 vTexCoord;
in vec3 vWorldPos;
in vec3 vWorldNormal;
in vec3 vViewPos;

out vec4 fragColor;

uniform sampler2D uTexture;
uniform sampler2DArray uShadowMap;
uniform bool uUseTexture;
uniform float uShadowBiasFactor;
uniform float uShadowBiasBase;
uniform float uShadowSpread;
uniform bool uEnableShadows;
uniform mat4 uLightSpaceMatrices[3];
uniform float uCascadePlaneDistances[3];
uniform vec3 uColor;
uniform vec3 uLightDir;     // direção da luz (apontando PARA a fonte)
uniform vec3 uLightColor;
uniform float uAmbient;

// Matriz de Poisson Disk para amostragem difusa (Soft Shadows)
const vec2 poissonDisk[16] = vec2[]( 
    vec2( -0.94201624, -0.39906216 ), vec2( 0.94558609, -0.76890725 ), 
    vec2( -0.094184101, -0.92938870 ), vec2( 0.34495938, 0.29387760 ), 
    vec2( -0.91588581, 0.45771432 ), vec2( -0.81544232, -0.87912464 ), 
    vec2( -0.38277543, 0.27676845 ), vec2( 0.97484398, 0.75648379 ), 
    vec2( 0.44323325, -0.97511554 ), vec2( 0.53742981, -0.47373420 ), 
    vec2( -0.26496911, -0.41893023 ), vec2( 0.79197514, 0.19090188 ), 
    vec2( -0.24188840, 0.99706507 ), vec2( -0.81409955, 0.91437590 ), 
    vec2( 0.19984126, 0.78641367 ), vec2( 0.14383161, -0.14100790 ) 
);

// Função de ruído para girar o disco de Poisson aleatoriamente por pixel (Dithering)
float interleaved_gradient_noise(vec2 position_screen) {
    vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    return fract(magic.z * fract(dot(position_screen, magic.xy)));
}

float ShadowCalculation(vec3 worldPos, vec3 normal, vec3 lightDir)
{
    // Seleciona a cascata baseada na profundidade da view da câmera
    int cascadeIndex = 2;
    for(int i = 0; i < 2; ++i)
    {
        if(abs(vViewPos.z) < uCascadePlaneDistances[i])
        {
            cascadeIndex = i;
            break;
        }
    }

    vec4 fragPosLightSpace = uLightSpaceMatrices[cascadeIndex] * vec4(worldPos, 1.0);

    // perform perspective divide
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    // transform to [0,1] range
    projCoords = projCoords * 0.5 + 0.5;
    
    if(projCoords.z > 1.0)
        return 0.0;
        
    // get depth of current fragment from light's perspective
    float currentDepth = projCoords.z;
    
    // calculate bias (based on depth map resolution and slope)
    float bias = max(uShadowBiasFactor * (1.0 - dot(normal, lightDir)), uShadowBiasBase);
    
    // Compensar o bias para mapas maiores que abrangem distancias longas
    if (cascadeIndex == 1) bias *= 1.5;
    if (cascadeIndex == 2) bias *= 3.0;
    
    // PCF (Poisson Disk com Interleaved Gradient Noise)
    float shadow = 0.0;
    vec2 texelSize = 1.0 / vec2(textureSize(uShadowMap, 0));
    
    // Calcula um angulo de rotacao aleatorio para este pixel
    float noise = interleaved_gradient_noise(gl_FragCoord.xy) * 6.28318530718; // 2 * PI
    float s = sin(noise);
    float c = cos(noise);
    mat2 rot = mat2(c, -s, s, c);
    
    // Tira 16 amostras do mapa de profundidade na camada específica
    for(int i = 0; i < 16; i++)
    {
        // Gira a amostra e aplica a dispersao (spread)
        vec2 offset = rot * poissonDisk[i] * (uShadowSpread * texelSize);
        float pcfDepth = texture(uShadowMap, vec3(projCoords.xy + offset, cascadeIndex)).r; 
        shadow += currentDepth - bias > pcfDepth ? 1.0 : 0.0;        
    }
    shadow /= 16.0;
    
    return shadow;
}

void main() {
    vec3 color = uUseTexture ? texture(uTexture, vTexCoord).rgb : uColor;
    
    // ambient
    vec3 ambient = uAmbient * uLightColor;
    
    // diffuse
    vec3 norm = normalize(vWorldNormal);
    vec3 lightDir = normalize(uLightDir);
    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * uLightColor;
    
    // calculate shadow
    float shadow = 0.0;
    if (uEnableShadows) {
        shadow = ShadowCalculation(vWorldPos, norm, lightDir);
    }
    
    vec3 result = (ambient + (1.0 - shadow) * diffuse) * color;
    fragColor = vec4(result, 1.0);
}