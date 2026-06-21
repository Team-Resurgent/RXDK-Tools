#include "stdafx.h"
#include "ShellObjectSite.h"

STDMETHODIMP CShellObjectWithSite::SetSite(IUnknown* pUnkSite)
{
    if (m_pUnknownSite)
    {
        m_pUnknownSite->Release();
        m_pUnknownSite = nullptr;
    }

    m_pUnknownSite = pUnkSite;
    if (m_pUnknownSite)
        m_pUnknownSite->AddRef();

    return S_OK;
}

STDMETHODIMP CShellObjectWithSite::GetSite(REFIID riid, void** ppvSite)
{
    if (!ppvSite)
        return E_POINTER;

    *ppvSite = nullptr;
    if (!m_pUnknownSite)
        return E_FAIL;

    return m_pUnknownSite->QueryInterface(riid, ppvSite);
}

STDMETHODIMP CShellObjectWithSite::GetService(REFGUID guidService, REFIID riid, void** ppvService)
{
    if (!ppvService)
        return E_POINTER;

    *ppvService = nullptr;
    if (!m_pUnknownSite)
        return E_FAIL;

    CComPtr<IServiceProvider> serviceProvider;
    RH(m_pUnknownSite->QueryInterface(IID_PPV_ARGS(&serviceProvider)));
    return serviceProvider->QueryService(guidService, riid, ppvService);
}
