Status:                                                                                                               
  - Proxy running at: http://127.0.0.1:8317                                                                             
  - API Key: scriptflow_gemini_pk_local                                                                                 
  - Available models: gemini-2.5-pro, gemini-2.5-flash, gemini-2.5-flash-lite, gemini-3-pro-preview,                    
  gemini-3-flash-preview                                                                                                
  - ScriptFlow builds successfully                                                                                      
                                                                                                                        
  To use with ScriptFlow:                                                                                               
  cd ScriptFlow                                                                                                         
  dotnet run -- new your-config.json                                                                                    
                                                                                                                        
  The GeminiProxyLLMClient will automatically connect to the local proxy. To switch between backends:                   
  # Use Gemini Proxy (default)                                                                                          
  SCRIPTFLOW_LLM_BACKEND=gemini-proxy dotnet run -- new config.json                                                     
                                                                                                                        
  # Use Claude Code CLI                                                                                                 
  SCRIPTFLOW_LLM_BACKEND=claude dotnet run -- new config.json                                                           
                                                                                                                        
  To manage the proxy:                                                                                                  
  # Start proxy                                                                                                         
  cd ~/CLIProxyAPI && ./cli-proxy-api                                                                                   
                                                                                                                        
  # Re-login (if token expires)                                                                                         
  cd ~/CLIProxyAPI && ./cli-proxy-api -login   
  cd ~/CLIProxyAPI && ./cli-proxy-api --antigravity-login

  # list login   
  ls -la ~/.ccs/cliproxy/auth/  
  
  # Option 1: Delete all auth files                                                                                                                                           
  rm ~/.ccs/cliproxy/auth/*.json  