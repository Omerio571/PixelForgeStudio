#include "raylib.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <regex>
#include <stdexcept>
#include <string>
#include <vector>

namespace fs = std::filesystem;

struct Frame { Rectangle rect; int duration; };
struct Asset { fs::path color, normal, emission; std::vector<Frame> frames; };

// This diagnostic reads the version 1 PixelForge export, whose rectangle
// objects contain these flat numeric fields. It is not a general JSON loader.
Asset ReadAsset(const fs::path& file)
{
    std::ifstream stream(file);
    if (!stream) throw std::runtime_error("Could not open the lighting manifest.");
    const std::string json((std::istreambuf_iterator<char>(stream)), {});
    auto field = [](const std::string& text, const std::string& key, const std::string& pattern) {
        std::smatch match;
        if (!std::regex_search(text, match, std::regex("\""+key+"\"\\s*:\\s*"+pattern)))
            throw std::runtime_error("Missing manifest field: "+key);
        return match[1].str();
    };
    auto number = [&](const std::string& text, const std::string& key) {
        return std::stoi(field(text,key,"([0-9]+)"));
    };
    const auto lighting = field(json,"lighting","(\\{[^}]*\\})");
    int version=number(lighting,"version");
    if ((version!=1 && version!=2) || field(lighting,"normalSpace","\"([^\"]+)\"")!="world")
        throw std::runtime_error("Expected world-space lighting manifest v1 or v2.");
    Asset asset{
        file.parent_path()/field(lighting,"color","\"([^\"]+)\""),
        file.parent_path()/field(lighting,"normal","\"([^\"]+)\""), {}, {}
    };
    if(version==2) asset.emission=file.parent_path()/field(lighting,"emission","\"([^\"]+)\"");
    const std::regex rect(R"rx(\{[^{}]*"durationMs"[^{}]*\{[^{}]*\}[^{}]*\})rx");
    for (auto it=std::sregex_iterator(json.begin(),json.end(),rect);it!=std::sregex_iterator();++it) {
        const auto entry=it->str();
        asset.frames.push_back({{float(number(entry,"x")),float(number(entry,"y")),
            float(number(entry,"width")),float(number(entry,"height"))},number(entry,"durationMs")});
    }
    if (asset.frames.empty() || int(asset.frames.size())!=number(json,"frameCount"))
        throw std::runtime_error("Invalid frame list.");
    return asset;
}

constexpr const char* fragment=R"glsl(
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform sampler2D normals;
uniform vec3 lightDir;
uniform sampler2D emissionMask;
uniform vec3 glowTint;
uniform float glowGain;
uniform float ambient;
out vec4 finalColor;
void main() {
    vec4 color = texture(texture0, fragTexCoord);
    vec3 N = normalize(texture(normals, fragTexCoord).rgb*2.0-1.0);
    float shade = ambient + (1.0-ambient)*max(dot(N,normalize(lightDir)),0.0);
    vec3 glow=glowTint*glowGain*texture(emissionMask,fragTexCoord).r;
    finalColor = vec4(clamp(color.rgb*shade+glow,0.0,1.0),color.a)*fragColor;
}
)glsl";

constexpr std::array<Vector3,5> lights{{{1,0,0},{-1,0,0},{0,1,0},{0,-1,0},{0,0,1}}};
constexpr std::array<const char*,5> names{{"East","West","North","South","Above"}};

struct Glow {Texture2D map; Vector3 tint; float gain; float ambient;};
constexpr std::array<Vector3,2> tints{{{1,.75f,.1f},{.2f,.7f,1}}};

void DrawLit(Texture2D color, Texture2D normal, Shader shader, int mapLoc, int lightLoc, Glow glow,
             Vector3 light, Rectangle frame, Rectangle dest)
{
    BeginShaderMode(shader);
    SetShaderValueTexture(shader,mapLoc,normal);
    SetShaderValue(shader,lightLoc,&light,SHADER_UNIFORM_VEC3);
    SetShaderValueTexture(shader,GetShaderLocation(shader,"emissionMask"),glow.map);
    SetShaderValue(shader,GetShaderLocation(shader,"glowTint"),&glow.tint,SHADER_UNIFORM_VEC3);
    SetShaderValue(shader,GetShaderLocation(shader,"glowGain"),&glow.gain,SHADER_UNIFORM_FLOAT);
    SetShaderValue(shader,GetShaderLocation(shader,"ambient"),&glow.ambient,SHADER_UNIFORM_FLOAT);
    DrawTexturePro(color,frame,dest,{},0,WHITE);
    // The active texture binding is released at batch flush.
    EndShaderMode();
}

void Verify(const Asset& asset, Texture2D color, Texture2D normals, Texture2D emission, Shader shader,
            int mapLoc, int lightLoc, const fs::path& output)
{
    fs::create_directories(output);
    Image source=LoadImage(asset.color.string().c_str());
    Image vectors=LoadImage(asset.normal.string().c_str());
    Image mask=asset.emission.empty()?Image{}:LoadImage(asset.emission.string().c_str());
    int width=int(asset.frames[0].rect.width),height=int(asset.frames[0].rect.height);
    RenderTexture2D target=LoadRenderTexture(width,height);
    for (size_t frame=0;frame<asset.frames.size();++frame) {
        for (size_t axis=0;axis<lights.size();++axis) {
          for(int variant=0;variant<(mask.data?3:1);++variant) {
            const auto rect=asset.frames[frame].rect;
            const Glow glow{emission,tints[variant==2?1:0],variant==0?0.f:.8f,mask.data?0.f:.25f};
            BeginTextureMode(target);
            ClearBackground(BLANK);
            DrawLit(color,normals,shader,mapLoc,lightLoc,glow,lights[axis],rect,{0,0,float(width),float(height)});
            EndTextureMode();
            Image result=LoadImageFromTexture(target.texture);
            ImageFlipVertical(&result);
            for(int y=0;y<height;++y) for(int x=0;x<width;++x) {
                const auto c=GetImageColor(source,int(rect.x)+x,int(rect.y)+y);
                const auto n=GetImageColor(vectors,int(rect.x)+x,int(rect.y)+y);
                const auto gpu=GetImageColor(result,x,y);
                if(c.a!=n.a || c.a!=gpu.a) throw std::runtime_error("Alpha, frame rectangle, or UV orientation mismatch.");
                const auto e=mask.data?GetImageColor(mask,int(rect.x)+x,int(rect.y)+y):Color{};
                if(mask.data && (e.a!=c.a||e.r!=e.g||e.g!=e.b))
                    throw std::runtime_error("Emission silhouette or grayscale mismatch.");
                if(!c.a) continue;
                const float nx=n.r/127.5f-1,ny=n.g/127.5f-1,nz=n.b/127.5f-1;
                const float length=std::sqrt(nx*nx+ny*ny+nz*nz);
                const auto l=lights[axis];
                const float shade=glow.ambient+(1-glow.ambient)*std::max(0.f,(nx*l.x+ny*l.y+nz*l.z)/length);
                const auto expected=[&](int base,float tint) {return std::min(255.f,base*shade+e.r*glow.gain*tint);};
                if(std::abs(gpu.r-expected(c.r,glow.tint.x))>2 || std::abs(gpu.g-expected(c.g,glow.tint.y))>2 || std::abs(gpu.b-expected(c.b,glow.tint.z))>2)
                    throw std::runtime_error("GPU diffuse result differs from linear byte decode.");
            }
            if(frame==0 && (variant==0||axis==1) && !ExportImage(result,(output/(std::string(names[axis])+(variant==1?"-warm":variant==2?"-cool":"")+".png")).string().c_str()))
                throw std::runtime_error("Could not save lighting evidence.");
            UnloadImage(result);
          }
        }
    }
    UnloadRenderTexture(target);
    UnloadImage(source);
    UnloadImage(vectors);
    if(mask.data)UnloadImage(mask);
    std::cout<<"Verified GPU colors and alpha for "<<asset.frames.size()<<" frames x five directions, with glow off and two tints for v2.\n";
}

int main(int argc,char** argv)
{
    if (argc!=2 && argc!=3) {
        std::cerr<<"Usage: lighting-validator <asset.lighting.json> [verification-image-folder]\n";
        return 2;
    }
    try {
        const auto asset=ReadAsset(fs::u8path(argv[1]));
        if(argc==3) SetConfigFlags(FLAG_WINDOW_HIDDEN);
        InitWindow(830,430,"PixelForge lighting validator");
        const auto color=LoadTexture(asset.color.string().c_str());
        const auto normals=LoadTexture(asset.normal.string().c_str());
        if(!IsTextureValid(color)||!IsTextureValid(normals)||color.width!=normals.width||color.height!=normals.height)
            throw std::runtime_error("Invalid or mismatched atlases.");
        const auto emission=asset.emission.empty()?normals:LoadTexture(asset.emission.string().c_str());
        if(!IsTextureValid(emission)||color.width!=emission.width||color.height!=emission.height)
            throw std::runtime_error("Invalid emission atlas.");
        SetTextureFilter(emission,TEXTURE_FILTER_POINT);
        SetTextureFilter(color,TEXTURE_FILTER_POINT);
        SetTextureFilter(normals,TEXTURE_FILTER_POINT);
        const auto shader=LoadShaderFromMemory(nullptr,fragment);
        const int mapLoc=GetShaderLocation(shader,"normals"),lightLoc=GetShaderLocation(shader,"lightDir");
        if(mapLoc<0||lightLoc<0)throw std::runtime_error("Could not compile the diffuse shader.");
        if(argc==3) Verify(asset,color,normals,emission,shader,mapLoc,lightLoc,fs::u8path(argv[2]));
        else {
            SetTargetFPS(60);
            size_t axis=0,frame=0;
            double elapsed=0;
            bool glowOn=!asset.emission.empty(),showMask=false;
            size_t tint=0;
            while(!WindowShouldClose()) {
                if(IsKeyPressed(KEY_RIGHT)) axis=(axis+1)%lights.size();
                if(IsKeyPressed(KEY_LEFT)) axis=(axis+lights.size()-1)%lights.size();
                if(!asset.emission.empty()&&IsKeyPressed(KEY_SPACE))glowOn=!glowOn;
                if(IsKeyPressed(KEY_C))tint=(tint+1)%tints.size();
                if(!asset.emission.empty()&&IsKeyPressed(KEY_E))showMask=!showMask;
                elapsed+=1000*GetFrameTime();
                if(elapsed>=asset.frames[frame].duration) {
                    elapsed-=asset.frames[frame].duration;
                    frame=(frame+1)%asset.frames.size();
                }
                const auto rect=asset.frames[frame].rect;
                const float zoom=std::max(1.f,std::floor(std::min(256/rect.width,320/rect.height)));
                const Rectangle dest{0,70,rect.width*zoom,rect.height*zoom};
                BeginDrawing();
                ClearBackground({37,40,47,255});
                DrawText("Arrows: light   Space: glow   C: tint   E: mask",20,20,20,RAYWHITE);
                DrawText("Base color",20,50,18,RAYWHITE);
                DrawText(showMask?"Emission mask":"Normal map",290,50,18,RAYWHITE);
                DrawText(TextFormat("Lit from %s",names[axis]),560,50,18,RAYWHITE);
                auto panel=dest;
                panel.x=20;DrawTexturePro(color,rect,panel,{},0,WHITE);
                panel.x=290;DrawTexturePro(showMask?emission:normals,rect,panel,{},0,WHITE);
                panel.x=560;DrawLit(color,normals,shader,mapLoc,lightLoc,{emission,tints[tint],glowOn?.8f:0.f,.25f},lights[axis],rect,panel);
                EndDrawing();
            }
        }
        UnloadShader(shader);
        if(!asset.emission.empty())UnloadTexture(emission);
        UnloadTexture(normals);
        UnloadTexture(color);
        CloseWindow();
        return 0;
    } catch(const std::exception& error) {
        std::cerr<<error.what()<<'\n';
        if(IsWindowReady()) CloseWindow();
        return 1;
    }
}
